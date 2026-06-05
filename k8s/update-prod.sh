#!/usr/bin/env bash
#
# update-prod.sh — MISE À JOUR de eShop en production, SANS interruption ni perte de données.
#
# Garanties :
#   - ZÉRO PERTE DE DONNÉES : migrations exécutées AVANT le rollout, via des Jobs,
#     et obligatoirement BACKWARD-COMPATIBLES (l'ancienne version des pods doit
#     continuer à fonctionner pendant et après la migration). Si une migration
#     échoue, on s'arrête : aucun rollout n'est lancé.
#   - ZÉRO INTERRUPTION : rollout progressif des Deployments. Les manifests fixent
#     strategy.rollingUpdate.maxUnavailable=0 (+ maxSurge=1, minReadySeconds=10,
#     readiness/liveness/startup probes, PDB minAvailable=1) : Kubernetes ne coupe
#     un ancien pod qu'une fois un nouveau pod Ready.
#
# Flux :
#   1. Déterminer TAG (sha git court, ou 1er argument)
#   2. build + push des 7 images vers GHCR
#   3. Jobs de migration (suffixés par TAG) + wait complete   <-- AVANT le rollout
#   4. kubectl set image des 7 Deployments + rollout status
#   5. (option) nettoyage des vieux Jobs de migration terminés
#
# Usage :
#   chmod +x k8s/update-prod.sh
#   GHCR_USER=lammensmichel GHCR_PAT=ghp_xxx ./k8s/update-prod.sh [TAG]
#
# Rollback (manuel, par service) :
#   kubectl rollout undo deployment/<svc> -n eshop
#   (les migrations étant backward-compatibles, revenir à l'image précédente est sûr.)
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Variables (à renseigner, ou via l'environnement)
# ---------------------------------------------------------------------------
REGISTRY="${REGISTRY:-ghcr.io/lammensmichel}"
NAMESPACE="${NAMESPACE:-eshop}"
GHCR_USER="${GHCR_USER:-}"
GHCR_PAT="${GHCR_PAT:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${SCRIPT_DIR}"

# TAG = 1er argument, sinon env TAG, sinon sha git court.
TAG="${1:-${TAG:-$(git rev-parse --short HEAD 2>/dev/null || echo latest)}}"

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

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m  ! \033[0m%s\n' "$*" >&2; }
die()  { printf '\033[1;31mERREUR:\033[0m %s\n' "$*" >&2; exit 1; }
kc() { kubectl -n "$NAMESPACE" "$@"; }

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
# 0. Vérifications
# ---------------------------------------------------------------------------
command -v kubectl >/dev/null 2>&1 || die "kubectl introuvable."
command -v docker  >/dev/null 2>&1 || die "docker introuvable."
[[ -n "$GHCR_USER" ]] || die "GHCR_USER non défini."
[[ -n "$GHCR_PAT" ]]  || die "GHCR_PAT non défini."
kc get deployment "${SERVICES[0]}" >/dev/null 2>&1 \
  || die "Le namespace ${NAMESPACE} ne semble pas déployé (deployment/${SERVICES[0]} absent). Lancez d'abord start-prod.sh."

log "Mise à jour eShop -> TAG=${TAG} (contexte kube : $(kubectl config current-context 2>/dev/null || echo '?'))"

# ---------------------------------------------------------------------------
# 1+2. build + push
# ---------------------------------------------------------------------------
build_and_push

# ---------------------------------------------------------------------------
# 3. Migrations AVANT le rollout (Jobs suffixés par TAG pour l'unicité) + wait complete
# ---------------------------------------------------------------------------
# On clone le manifest des migrations, on substitue le TAG d'image, puis on suffixe
# les noms des Jobs par le TAG (un Job est immuable : un nom unique par release).
log "Migrations (backward-compatibles) AVANT le rollout"
MIG_TMP="$(mktemp)"
sed "s|__TAG__|${TAG}|g" "${K8S_DIR}/jobs/migrations.yaml" > "${MIG_TMP}"
# Suffixe les noms de Jobs : metadata.name: migrate-xxx -> migrate-xxx-<TAG>
sed -i.bak -E "s|^([[:space:]]*name:[[:space:]]*migrate-(catalog\|ordering\|identity))[[:space:]]*$|\1-${TAG}|" "${MIG_TMP}"
rm -f "${MIG_TMP}.bak"

kubectl apply -f "${MIG_TMP}"
rm -f "${MIG_TMP}"

for base in migrate-catalog migrate-ordering migrate-identity; do
  job="${base}-${TAG}"
  log "  attente job/${job}"
  if ! kc wait --for=condition=complete "job/${job}" --timeout=300s; then
    warn "Migration ${job} échouée. Logs :"
    kc logs "job/${job}" --tail=100 || true
    die "Migration échouée — AUCUN rollout effectué (état stable conservé)."
  fi
  ok "job/${job} terminé"
done

# ---------------------------------------------------------------------------
# 4. Rollout zéro-interruption : nouvelle image sur chaque Deployment
# ---------------------------------------------------------------------------
log "Rollout des Deployments (maxUnavailable=0 => zéro-interruption)"
for svc in "${SERVICES[@]}"; do
  kc set image "deployment/${svc}" "${svc}=${REGISTRY}/${svc}:${TAG}"
done
for svc in "${SERVICES[@]}"; do
  log "  rollout status deployment/${svc}"
  if ! kc rollout status "deployment/${svc}" --timeout=300s; then
    warn "Le rollout de ${svc} a échoué/timeout."
    warn "  Rollback de ce service : kubectl rollout undo deployment/${svc} -n ${NAMESPACE}"
    die "Rollout ${svc} non abouti."
  fi
  ok "deployment/${svc} à jour"
done

# ---------------------------------------------------------------------------
# 5. Nettoyage (optionnel) des vieux Jobs de migration terminés
# ---------------------------------------------------------------------------
log "Nettoyage des anciens Jobs de migration terminés (on conserve le TAG courant)"
for base in migrate-catalog migrate-ordering migrate-identity; do
  while read -r oldjob; do
    [[ -z "$oldjob" ]] && continue
    [[ "$oldjob" == "${base}-${TAG}" ]] && continue   # garder la release courante
    kc delete job "$oldjob" --ignore-not-found >/dev/null 2>&1 || true
    ok "supprimé $oldjob"
  done < <(kc get jobs -o name 2>/dev/null | sed 's|job.batch/||' | grep -E "^${base}-" || true)
done

echo
log "Mise à jour terminée — TAG ${TAG} en service."
ok "Front : https://app.<DOMAIN>   |   Identity : https://id.<DOMAIN>"
echo "  Rollback éventuel : kubectl rollout undo deployment/<svc> -n ${NAMESPACE}"
