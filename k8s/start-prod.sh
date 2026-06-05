#!/usr/bin/env bash
#
# start-prod.sh — PREMIER déploiement de eShop en production (Kubernetes).
#
# Idempotent : peut être rejoué sans casser un cluster déjà déployé.
# Respecte le contrat k8s/CONTRACT.md À LA LETTRE (noms, chemins, clés).
#
# Flux :
#   1. Vérifications préalables (kubectl/docker, cert-manager, ingress-nginx, contexte kube)
#   2. (option --build) build + push des 7 images vers GHCR
#   3. namespace
#   4. imagePullSecret ghcr-creds
#   5. Secret eshop-secrets (génère des mots de passe forts s'il est absent)
#   6. Secret eshop-connstrings (composé à partir des mots de passe)
#   7. ConfigMaps (substitution __DOMAIN__ / __ACME_EMAIL__) + ClusterIssuer
#   8. Infra (StatefulSets) + attente Ready
#   9. Jobs de migration (substitution __TAG__) + wait complete
#  10. Apps (Deployments/Services/PDB/HPA) + Ingress (substitution __TAG__/__DOMAIN__) + rollout status
#
# Usage :
#   chmod +x k8s/start-prod.sh
#   DOMAIN=eshop.example.com ACME_EMAIL=admin@example.com \
#   GHCR_USER=lammensmichel GHCR_PAT=ghp_xxx \
#   ./k8s/start-prod.sh [--build]
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Variables (à renseigner, ou via l'environnement)
# ---------------------------------------------------------------------------
DOMAIN="${DOMAIN:-}"                              # ex. eshop.example.com  (OBLIGATOIRE)
ACME_EMAIL="${ACME_EMAIL:-}"                      # email Let's Encrypt    (OBLIGATOIRE)
REGISTRY="${REGISTRY:-ghcr.io/lammensmichel}"     # registre d'images
NAMESPACE="${NAMESPACE:-eshop}"
GHCR_USER="${GHCR_USER:-}"                         # utilisateur GitHub     (OBLIGATOIRE)
GHCR_PAT="${GHCR_PAT:-}"                           # PAT GitHub (read+write packages) (OBLIGATOIRE)

# TAG par défaut = sha git court de l'arbre courant.
TAG="${TAG:-$(git rev-parse --short HEAD 2>/dev/null || echo latest)}"

# Racine du repo = parent de ce script (les Dockerfiles utilisent ce contexte).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${SCRIPT_DIR}"

# Mapping service -> répertoire du Dockerfile (contexte = racine du repo).
SERVICES=(catalog-api basket-api ordering-api identity-api webapp orderprocessor paymentprocessor)
declare -A DOCKER_CTX=(
  [catalog-api]="src/Catalog.API"
  [basket-api]="src/Basket.API"
  [ordering-api]="src/Ordering.API"
  [identity-api]="src/Identity.API"
  [webapp]="src/WebApp.Server"
  [orderprocessor]="src/OrderProcessor"
  [paymentprocessor]="src/PaymentProcessor"
)

BUILD=false
for arg in "$@"; do
  case "$arg" in
    --build) BUILD=true ;;
    *) echo "Argument inconnu : $arg" >&2; exit 2 ;;
  esac
done

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m  ! \033[0m%s\n' "$*" >&2; }
die()  { printf '\033[1;31mERREUR:\033[0m %s\n' "$*" >&2; exit 1; }

confirm() {
  # confirm "question" -> renvoie 0 si l'utilisateur tape "oui"/"y"
  local prompt="$1"
  read -r -p "$prompt [oui/non] " ans
  case "$ans" in
    oui|o|y|yes|Y) return 0 ;;
    *) return 1 ;;
  esac
}

kc() { kubectl -n "$NAMESPACE" "$@"; }

# Applique un manifest en substituant des placeholders (sur une copie temporaire).
# usage: apply_substituted <fichier> [sed-expr ...]
apply_substituted() {
  local file="$1"; shift
  [[ -f "$file" ]] || die "Manifest introuvable : $file"
  local tmp; tmp="$(mktemp)"
  cp "$file" "$tmp"
  while [[ $# -gt 0 ]]; do
    sed -i.bak "$1" "$tmp" && rm -f "${tmp}.bak"
    shift
  done
  kubectl apply -f "$tmp"
  rm -f "$tmp"
}

# Build + push factorisé (identique à update-prod.sh).
build_and_push() {
  log "Build + push des images (TAG=${TAG}) vers ${REGISTRY}"
  echo "${GHCR_PAT}" | docker login ghcr.io -u "${GHCR_USER}" --password-stdin
  local svc ctx
  for svc in "${SERVICES[@]}"; do
    ctx="${DOCKER_CTX[$svc]}"
    log "  build ${svc} (-f ${ctx}/Dockerfile)"
    docker build -f "${REPO_ROOT}/${ctx}/Dockerfile" \
      -t "${REGISTRY}/${svc}:${TAG}" \
      "${REPO_ROOT}"
    docker push "${REGISTRY}/${svc}:${TAG}"
    ok "${REGISTRY}/${svc}:${TAG}"
  done
}

# ---------------------------------------------------------------------------
# 1. Vérifications préalables
# ---------------------------------------------------------------------------
log "Vérifications préalables"

command -v kubectl >/dev/null 2>&1 || die "kubectl introuvable dans le PATH."
$BUILD && { command -v docker >/dev/null 2>&1 || die "docker introuvable (requis pour --build)."; }
ok "kubectl présent"

[[ -n "$DOMAIN" ]]     || die "DOMAIN non défini (ex. DOMAIN=eshop.example.com)."
[[ -n "$ACME_EMAIL" ]] || die "ACME_EMAIL non défini (email Let's Encrypt)."
[[ -n "$GHCR_USER" ]]  || die "GHCR_USER non défini."
[[ -n "$GHCR_PAT" ]]   || die "GHCR_PAT non défini (PAT GitHub avec write:packages)."
ok "Variables obligatoires présentes (DOMAIN=${DOMAIN})"

# cert-manager présent ?
if ! kubectl get crd clusterissuers.cert-manager.io >/dev/null 2>&1; then
  warn "cert-manager NON détecté (CRD clusterissuers.cert-manager.io absente)."
  warn "  Installation :"
  warn "    kubectl apply -f https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml"
  confirm "Continuer quand même (le ClusterIssuer / TLS échoueront sans cert-manager) ?" || die "Annulé."
else
  ok "cert-manager détecté"
fi

# ingress-nginx présent ?
if ! kubectl get ingressclass nginx >/dev/null 2>&1; then
  warn "ingress-nginx NON détecté (IngressClass 'nginx' absente)."
  warn "  Installation :"
  warn "    helm upgrade --install ingress-nginx ingress-nginx \\"
  warn "      --repo https://kubernetes.github.io/ingress-nginx \\"
  warn "      --namespace ingress-nginx --create-namespace"
  confirm "Continuer quand même (l'Ingress ne sera pas servi sans ingress-nginx) ?" || die "Annulé."
else
  ok "ingress-nginx détecté (IngressClass 'nginx')"
fi

# Contexte kube courant + confirmation (action sur un cluster).
CURRENT_CTX="$(kubectl config current-context 2>/dev/null || echo '<aucun>')"
log "Contexte kube courant : ${CURRENT_CTX}"
confirm "Déployer eShop (premier déploiement) sur ce cluster ?" || die "Annulé par l'utilisateur."

# ---------------------------------------------------------------------------
# 2. (option) build + push
# ---------------------------------------------------------------------------
if $BUILD; then
  build_and_push
else
  log "Mode sans --build : les images ${REGISTRY}/<svc>:${TAG} sont supposées déjà poussées."
fi

# ---------------------------------------------------------------------------
# 3. Namespace
# ---------------------------------------------------------------------------
log "Namespace ${NAMESPACE}"
kubectl apply -f "${K8S_DIR}/00-namespace.yaml"
ok "namespace appliqué"

# ---------------------------------------------------------------------------
# 4. imagePullSecret ghcr-creds (idempotent : delete + create)
# ---------------------------------------------------------------------------
log "imagePullSecret ghcr-creds"
kc delete secret ghcr-creds --ignore-not-found
kc create secret docker-registry ghcr-creds \
  --docker-server=ghcr.io \
  --docker-username="${GHCR_USER}" \
  --docker-password="${GHCR_PAT}" \
  --docker-email="${GHCR_USER}@users.noreply.github.com"
kc label secret ghcr-creds app.kubernetes.io/part-of=eshop --overwrite >/dev/null
ok "ghcr-creds (re)créé"

# ---------------------------------------------------------------------------
# 5. Secret eshop-secrets — généré s'il est absent, sinon laissé tel quel
# ---------------------------------------------------------------------------
log "Secret eshop-secrets"
if kc get secret eshop-secrets >/dev/null 2>&1; then
  ok "eshop-secrets existe déjà — conservé tel quel (mots de passe inchangés)."
  PG_PWD="$(kc get secret eshop-secrets -o jsonpath='{.data.postgres-password}' | base64 -d)"
  REDIS_PWD="$(kc get secret eshop-secrets -o jsonpath='{.data.redis-password}' | base64 -d)"
  RMQ_USER="$(kc get secret eshop-secrets -o jsonpath='{.data.rabbitmq-user}' | base64 -d)"
  RMQ_PWD="$(kc get secret eshop-secrets -o jsonpath='{.data.rabbitmq-password}' | base64 -d)"
else
  log "  génération de mots de passe forts (openssl rand)"
  PG_PWD="$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)"
  REDIS_PWD="$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)"
  RMQ_USER="eshop"
  RMQ_PWD="$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)"
  DEMO_PWD="$(openssl rand -base64 18 | tr -d '/+=' | head -c 24)"
  kc create secret generic eshop-secrets \
    --from-literal=postgres-password="${PG_PWD}" \
    --from-literal=redis-password="${REDIS_PWD}" \
    --from-literal=rabbitmq-user="${RMQ_USER}" \
    --from-literal=rabbitmq-password="${RMQ_PWD}" \
    --from-literal=identity-demo-password="${DEMO_PWD}"
  kc label secret eshop-secrets app.kubernetes.io/part-of=eshop --overwrite >/dev/null
  ok "eshop-secrets créé (mots de passe générés)"
fi

# ---------------------------------------------------------------------------
# 6. Secret eshop-connstrings — composé à partir des mots de passe (chaînes EXACTES du contrat)
# ---------------------------------------------------------------------------
log "Secret eshop-connstrings (composé)"
CS_CATALOG="Host=postgres;Port=5432;Database=catalogdb;Username=postgres;Password=${PG_PWD}"
CS_ORDERING="Host=postgres;Port=5432;Database=orderingdb;Username=postgres;Password=${PG_PWD}"
CS_IDENTITY="Host=postgres;Port=5432;Database=identitydb;Username=postgres;Password=${PG_PWD}"
CS_RABBITMQ="amqp://${RMQ_USER}:${RMQ_PWD}@rabbitmq:5672"
CS_REDIS="redis:6379,password=${REDIS_PWD}"

# apply (et non create) pour rester idempotent et refléter une éventuelle rotation des mdp.
kc create secret generic eshop-connstrings \
  --from-literal=ConnectionStrings__catalogdb="${CS_CATALOG}" \
  --from-literal=ConnectionStrings__orderingdb="${CS_ORDERING}" \
  --from-literal=ConnectionStrings__identitydb="${CS_IDENTITY}" \
  --from-literal=ConnectionStrings__rabbitmq="${CS_RABBITMQ}" \
  --from-literal=ConnectionStrings__redis="${CS_REDIS}" \
  --dry-run=client -o yaml | kubectl apply -f -
kc label secret eshop-connstrings app.kubernetes.io/part-of=eshop --overwrite >/dev/null
ok "eshop-connstrings appliqué"

# ---------------------------------------------------------------------------
# 7. ConfigMaps + ClusterIssuer (substitution __DOMAIN__ / __ACME_EMAIL__)
# ---------------------------------------------------------------------------
log "ConfigMaps + ClusterIssuer (substitution des placeholders)"
apply_substituted "${K8S_DIR}/config/eshop-config.yaml"       "s|__DOMAIN__|${DOMAIN}|g"
apply_substituted "${K8S_DIR}/config/webapp-appsettings.yaml"  "s|__DOMAIN__|${DOMAIN}|g"
apply_substituted "${K8S_DIR}/config/cluster-issuer.yaml" \
  "s|__DOMAIN__|${DOMAIN}|g" "s|__ACME_EMAIL__|${ACME_EMAIL}|g"
ok "eshop-config, webapp-appsettings, cluster-issuer appliqués"

# ---------------------------------------------------------------------------
# 8. Infrastructure (StatefulSets : postgres, redis, rabbitmq) + attente Ready
# ---------------------------------------------------------------------------
log "Infrastructure (StatefulSets)"
for f in "${K8S_DIR}"/infra/*; do
  apply_substituted "$f"
done
for sts in postgres redis rabbitmq; do
  log "  attente rollout statefulset/${sts}"
  kc rollout status "statefulset/${sts}" --timeout=300s
  ok "statefulset/${sts} prêt"
done

# ---------------------------------------------------------------------------
# 9. Jobs de migration (substitution __TAG__) + wait complete
# ---------------------------------------------------------------------------
log "Jobs de migration (TAG=${TAG})"
apply_substituted "${K8S_DIR}/jobs/migrations.yaml" "s|__TAG__|${TAG}|g"
for job in migrate-catalog migrate-ordering migrate-identity; do
  log "  attente job/${job}"
  if ! kc wait --for=condition=complete "job/${job}" --timeout=300s; then
    warn "Le job ${job} n'a pas réussi. Logs :"
    kc logs "job/${job}" --tail=100 || true
    die "Migration ${job} échouée — déploiement interrompu (pas de rollout des apps)."
  fi
  ok "job/${job} terminé"
done

# ---------------------------------------------------------------------------
# 10. Apps (Deployments/Services/PDB/HPA) + Ingress + rollout status
# ---------------------------------------------------------------------------
log "Applications (substitution __TAG__ / __DOMAIN__)"
for f in "${K8S_DIR}"/apps/*; do
  apply_substituted "$f" "s|__TAG__|${TAG}|g" "s|__DOMAIN__|${DOMAIN}|g"
done
apply_substituted "${K8S_DIR}/ingress.yaml" "s|__DOMAIN__|${DOMAIN}|g"
ok "apps + ingress appliqués"

for svc in "${SERVICES[@]}"; do
  log "  rollout status deployment/${svc}"
  kc rollout status "deployment/${svc}" --timeout=300s
  ok "deployment/${svc} disponible"
done

# ---------------------------------------------------------------------------
# Final : URL + rappel DNS
# ---------------------------------------------------------------------------
INGRESS_IP="$(kubectl get svc -A -o jsonpath='{range .items[?(@.spec.type=="LoadBalancer")]}{.metadata.namespace}/{.metadata.name}: {.status.loadBalancer.ingress[0].ip}{.status.loadBalancer.ingress[0].hostname}{"\n"}{end}' 2>/dev/null | grep -i ingress || true)"

echo
log "Déploiement terminé."
ok "Front      : https://app.${DOMAIN}"
ok "Identity   : https://id.${DOMAIN}"
echo
warn "RAPPEL DNS : faites pointer 'app.${DOMAIN}' ET 'id.${DOMAIN}' vers l'IP publique de l'ingress-nginx."
if [[ -n "$INGRESS_IP" ]]; then
  echo "  IP / hostname du LoadBalancer ingress détecté :"
  echo "$INGRESS_IP" | sed 's/^/    /'
else
  echo "  Récupérez l'IP avec : kubectl get svc -n ingress-nginx"
fi
echo "  Le certificat Let's Encrypt ne sera émis qu'une fois le DNS résolu (HTTP-01)."
