# Déploiement LOCAL (minikube) — eShop

Variante **locale** de la prod, pour faire tourner toute la stack sur ta machine
sans domaine public ni Let's Encrypt. Idéal pour voir l'ensemble fonctionner
« comme en prod » mais en local.

## Différences avec la prod
| | Prod (`../start-prod.sh`) | Local (`./start-local.sh`) |
|---|---|---|
| Cluster | cloud (GKE/EKS/AKS…) | **minikube** |
| Images | poussées sur **GHCR** | **construites dans le démon Docker de minikube** (aucun push) |
| Domaine | ton domaine public | **nip.io** : `app.127.0.0.1.nip.io` / `id.127.0.0.1.nip.io` |
| TLS | **Let's Encrypt** (valide) | **auto-signé** (cert-manager `selfsigned`) |
| OIDC | normal | métadonnées OIDC récupérées en interne (voir ci-dessous) ; certificat à accepter dans le navigateur |
| Users démo | désactivés | **activés** (alice/bob, `Pass123$`) pour pouvoir se connecter |

## Pré-requis
- `docker`, `kubectl`, `minikube` installés.
- Assez de ressources (le script démarre minikube avec 4 CPU / 6 Go).

## Lancement
```bash
cd k8s/local
chmod +x start-local.sh
./start-local.sh
```
Le script : démarre minikube → installe cert-manager (si absent) + l'addon ingress →
construit les 7 images dans minikube → crée secrets/configmaps (domaine nip.io) →
déploie l'infra (Postgres/Redis/RabbitMQ) → joue les **Jobs de migration** →
déploie les applications + l'Ingress (TLS auto-signé).

## Accès (2 étapes manuelles, propres à macOS/minikube)
1. **Tunnel** — dans un AUTRE terminal (le laisser tourner ; demande sudo) :
   ```bash
   minikube tunnel
   ```
   Il expose l'Ingress sur `127.0.0.1:80/443` (ce que `*.127.0.0.1.nip.io` résout).
2. **Navigateur** — ouvre **https://app.127.0.0.1.nip.io** :
   - accepte le **certificat auto-signé** (avertissement) pour les **deux** hôtes :
     `https://app.127.0.0.1.nip.io` **et** `https://id.127.0.0.1.nip.io`
     (le plus simple : visite d'abord `https://id.127.0.0.1.nip.io` et accepte, puis l'app) ;
   - connecte-toi : **alice / Pass123$** (Admin) ou **bob / Pass123$** (Customer) ;
   - Catalogue → Panier → Checkout (carte `4111 1111 1111 1111`) → Commandes.

> Pourquoi HTTPS même en local ? Le flux OIDC **PKCE** utilise `crypto.subtle` du
> navigateur, qui n'est disponible qu'en **contexte sécurisé** (HTTPS). D'où le TLS
> auto-signé. Les APIs, elles, lisent les métadonnées OIDC **en interne** (HTTP, DNS
> du Service `identity-api`), donc ne sont pas gênées par le certificat auto-signé ;
> l'issuer reste l'URL publique grâce à `Identity__IssuerUri`.

## Diagnostic
```bash
kubectl -n eshop get pods                 # état des pods
kubectl -n eshop logs deploy/ordering-api # logs d'un service
kubectl -n eshop get ingress              # routes + hôtes
```

## Tout supprimer
```bash
minikube delete   # efface le cluster local (et toutes les données)
```

## Note Docker Desktop Kubernetes (alternative)
Si tu préfères le Kubernetes intégré à Docker Desktop : active-le dans Settings →
Kubernetes, puis adapte `start-local.sh` (les images sont déjà dans le démon partagé,
donc pas de `minikube docker-env` ; l'Ingress s'expose sur `localhost`). Le principe
(nip.io + TLS auto-signé + métadonnées internes) reste identique.
