#!/usr/bin/env bash
# =============================================================================
# start-local.sh — déploie eShop sur un cluster LOCAL minikube (macOS/Linux).
# -----------------------------------------------------------------------------
# Variante locale de start-prod.sh : pas de domaine public, pas de Let's Encrypt,
# pas de registre distant. À la place :
#   - images CONSTRUITES directement dans le démon Docker de minikube (aucun push) ;
#   - domaine via nip.io sur 127.0.0.1 (app.127.0.0.1.nip.io / id.127.0.0.1.nip.io) ;
#   - TLS AUTO-SIGNÉ via cert-manager (l'OIDC/PKCE exige HTTPS = contexte sécurisé) ;
#   - métadonnées OIDC récupérées en INTERNE par les APIs (Identity__MetadataAddress).
#
# ⚠️ Sur macOS, l'Ingress minikube n'est joignable que via `minikube tunnel`
#    (à lancer dans un AUTRE terminal, demande le mot de passe sudo). Voir la fin.
#
# Compatible bash 3.2 (macOS) : pas de tableaux associatifs.
# Pré-requis : docker, kubectl, minikube installés.
# Usage : cd k8s/local && ./start-local.sh
# =============================================================================
set -euo pipefail

NAMESPACE=eshop
TAG=local
REGISTRY=ghcr.io/lammensmichel          # nom des images (local, non poussé)
DOMAIN=127.0.0.1.nip.io                  # -> app.127.0.0.1.nip.io / id.127.0.0.1.nip.io
CERT_MANAGER_VERSION=v1.16.2
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
K8S_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"  # k8s/
ROOT_DIR="$(cd "$K8S_DIR/.." && pwd)"    # racine du dépôt

# Les 7 services à construire/déployer.
SERVICES="catalog-api basket-api ordering-api identity-api webapp orderprocessor paymentprocessor"

# Mapping service -> dossier projet (sans tableau associatif, pour bash 3.2).
svc_dir() {
  case "$1" in
    catalog-api)      echo src/Catalog.API ;;
    basket-api)       echo src/Basket.API ;;
    ordering-api)     echo src/Ordering.API ;;
    identity-api)     echo src/Identity.API ;;
    webapp)           echo src/WebApp.Server ;;
    orderprocessor)   echo src/OrderProcessor ;;
    paymentprocessor) echo src/PaymentProcessor ;;
    *) echo "??" ;;
  esac
}

say() { printf "\n\033[1;36m==> %s\033[0m\n" "$*"; }

# ---------------------------------------------------------------------------
say "Vérification des outils"
for t in docker kubectl minikube; do command -v "$t" >/dev/null || { echo "❌ $t manquant"; exit 1; }; done

# ---------------------------------------------------------------------------
say "Démarrage de minikube (si nécessaire)"
# Check FIABLE : on interroge réellement l'API du cluster via kubectl (le simple
# `minikube status` peut renvoyer 0 sur un profil corrompu dont le conteneur a disparu).
if ! kubectl --context minikube get nodes >/dev/null 2>&1; then
  minikube delete >/dev/null 2>&1 || true   # purge un éventuel état corrompu
  minikube start --driver=docker --cpus=3 --memory=4096 --addons=ingress
else
  echo "minikube déjà démarré."
fi
kubectl config use-context minikube >/dev/null
minikube addons enable ingress >/dev/null

# ---------------------------------------------------------------------------
say "Installation de cert-manager (si absent)"
if ! kubectl get ns cert-manager >/dev/null 2>&1; then
  kubectl apply -f "https://github.com/cert-manager/cert-manager/releases/download/${CERT_MANAGER_VERSION}/cert-manager.yaml"
  echo "Attente que cert-manager soit prêt..."
  kubectl -n cert-manager rollout status deploy/cert-manager-webhook --timeout=180s
  kubectl -n cert-manager rollout status deploy/cert-manager --timeout=180s
else
  echo "cert-manager déjà présent."
fi

# ---------------------------------------------------------------------------
say "Construction des 7 images DANS le démon Docker de minikube (aucun push)"
eval "$(minikube docker-env)"
for s in $SERVICES; do
  d="$(svc_dir "$s")"
  echo "  build $REGISTRY/$s:$TAG  (-f $d/Dockerfile)"
  docker build -q -f "$ROOT_DIR/$d/Dockerfile" -t "$REGISTRY/$s:$TAG" "$ROOT_DIR" >/dev/null
done
eval "$(minikube docker-env -u)"

# ---------------------------------------------------------------------------
say "Namespace + secrets"
kubectl apply -f "$K8S_DIR/00-namespace.yaml"

# imagePullSecret factice : les images sont LOCALES (imagePullPolicy IfNotPresent),
# mais les manifests référencent ghcr-creds -> on crée un secret bidon pour résoudre la référence.
kubectl -n "$NAMESPACE" create secret docker-registry ghcr-creds \
  --docker-server=ghcr.io --docker-username=local --docker-password=local \
  --dry-run=client -o yaml | kubectl apply -f -

# Mots de passe (générés une fois, conservés ensuite)
gen() { openssl rand -base64 18 | tr -d '/+=' | head -c 24; }
if ! kubectl -n "$NAMESPACE" get secret eshop-secrets >/dev/null 2>&1; then
  PG_PWD=$(gen); REDIS_PWD=$(gen); RMQ_PWD=$(gen)
  kubectl -n "$NAMESPACE" create secret generic eshop-secrets \
    --from-literal=postgres-password="$PG_PWD" \
    --from-literal=redis-password="$REDIS_PWD" \
    --from-literal=rabbitmq-user=eshop \
    --from-literal=rabbitmq-password="$RMQ_PWD" \
    --from-literal=identity-demo-password='Pass123$'
else
  echo "eshop-secrets existe déjà (conservé)."
fi

# Clé de signature des jetons (RSA) — partagée par toutes les répliques d'Identity,
# montée en lecture seule (compatible readOnlyRootFilesystem). Générée une seule fois.
if ! kubectl -n "$NAMESPACE" get secret eshop-signing-key >/dev/null 2>&1; then
  openssl genrsa 2048 > /tmp/eshop-signing.key 2>/dev/null
  kubectl -n "$NAMESPACE" create secret generic eshop-signing-key --from-file=signing.key=/tmp/eshop-signing.key
  rm -f /tmp/eshop-signing.key
else
  echo "eshop-signing-key existe déjà (conservé)."
fi

PG_PWD=$(kubectl -n "$NAMESPACE" get secret eshop-secrets -o jsonpath='{.data.postgres-password}' | base64 -d)
REDIS_PWD=$(kubectl -n "$NAMESPACE" get secret eshop-secrets -o jsonpath='{.data.redis-password}' | base64 -d)
RMQ_USER=$(kubectl -n "$NAMESPACE" get secret eshop-secrets -o jsonpath='{.data.rabbitmq-user}' | base64 -d)
RMQ_PWD=$(kubectl -n "$NAMESPACE" get secret eshop-secrets -o jsonpath='{.data.rabbitmq-password}' | base64 -d)

# Chaînes de connexion (DNS interne des Services K8s)
kubectl -n "$NAMESPACE" create secret generic eshop-connstrings \
  --from-literal=ConnectionStrings__catalogdb="Host=postgres;Port=5432;Database=catalogdb;Username=postgres;Password=$PG_PWD" \
  --from-literal=ConnectionStrings__orderingdb="Host=postgres;Port=5432;Database=orderingdb;Username=postgres;Password=$PG_PWD" \
  --from-literal=ConnectionStrings__identitydb="Host=postgres;Port=5432;Database=identitydb;Username=postgres;Password=$PG_PWD" \
  --from-literal=ConnectionStrings__rabbitmq="amqp://$RMQ_USER:$RMQ_PWD@rabbitmq:5672" \
  --from-literal=ConnectionStrings__redis="redis:6379,password=$REDIS_PWD" \
  --dry-run=client -o yaml | kubectl apply -f -

# ---------------------------------------------------------------------------
say "ConfigMaps (domaine local + seed démo activé pour pouvoir se connecter)"
# eshop-config : on part du manifest prod, on substitue le domaine, et on FORCE
# SeedDemoUsers=true (pour avoir alice/bob en local).
sed -e "s/__DOMAIN__/$DOMAIN/g" \
    -e 's/Identity__SeedDemoUsers: "false"/Identity__SeedDemoUsers: "true"/' \
    "$K8S_DIR/config/eshop-config.yaml" | kubectl apply -f -
sed -e "s/__DOMAIN__/$DOMAIN/g" "$K8S_DIR/config/webapp-appsettings.yaml" | kubectl apply -f -

# Émetteur TLS auto-signé (local)
kubectl apply -f "$SCRIPT_DIR/selfsigned-issuer.yaml"

# ---------------------------------------------------------------------------
say "Infrastructure (Postgres / Redis / RabbitMQ) + attente"
kubectl apply -f "$K8S_DIR/infra/"
for sts in postgres redis rabbitmq; do
  kubectl -n "$NAMESPACE" rollout status statefulset/$sts --timeout=300s
done

# ---------------------------------------------------------------------------
say "Migrations (Jobs --migrate) avant le déploiement des apps"
sed -e "s/__TAG__/$TAG/g" "$K8S_DIR/jobs/migrations.yaml" | kubectl apply -f -
for j in migrate-catalog migrate-ordering migrate-identity; do
  echo "  attente du Job $j..."
  kubectl -n "$NAMESPACE" wait --for=condition=complete --timeout=240s job/$j || {
    echo "❌ Job $j en échec"; kubectl -n "$NAMESPACE" logs job/$j --tail=40 || true; exit 1; }
done

# ---------------------------------------------------------------------------
say "Applications (images locales) + Ingress (auto-signé)"
# imagePullPolicy IfNotPresent par défaut (tag != latest) -> utilise l'image locale construite.
for f in "$K8S_DIR"/apps/*.yaml; do
  sed -e "s/__TAG__/$TAG/g" -e "s/__DOMAIN__/$DOMAIN/g" "$f" | kubectl apply -f -
done
# Ingress : on bascule l'annotation cert-manager de letsencrypt-prod -> selfsigned (local)
sed -e "s/__DOMAIN__/$DOMAIN/g" \
    -e 's/cluster-issuer: letsencrypt-prod/cluster-issuer: selfsigned/' \
    "$K8S_DIR/ingress.yaml" | kubectl apply -f -

# NB : pas besoin d'accepter le cert auto-signé côté APIs — elles récupèrent les
# métadonnées OIDC en INTERNE (Identity__MetadataAddress=http://identity-api:8080/...,
# fourni par le ConfigMap eshop-config), donc aucune TLS auto-signée n'est touchée
# côté serveur. Seul le NAVIGATEUR voit le certificat auto-signé (à accepter une fois).

say "Attente du déploiement des applications"
for s in $SERVICES; do
  kubectl -n "$NAMESPACE" rollout status deployment/$s --timeout=300s
done

# ---------------------------------------------------------------------------
say "Déploiement local terminé."
cat <<EOF

ETAPES MANUELLES (macOS/minikube) :
  1) Dans un AUTRE terminal, lance le tunnel (laisse-le tourner ; il demande sudo) :
        minikube tunnel
  2) Ouvre le front :  https://app.$DOMAIN
     -> Accepte le certificat AUTO-SIGNE (avertissement navigateur) pour les DEUX hotes :
        https://app.$DOMAIN  ET  https://id.$DOMAIN
  3) Connecte-toi : alice / Pass123\$  (Admin)  ou  bob / Pass123\$  (Customer).
     Panier -> Checkout (carte 4111 1111 1111 1111) -> Commandes (auto-rafraichies).

Diagnostic :  kubectl -n $NAMESPACE get pods
Suppression : minikube delete   (efface tout le cluster local)
EOF
