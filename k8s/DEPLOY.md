# Déploiement eShop en production (Kubernetes)

Guide opérationnel pour déployer et maintenir eShop sur un cluster Kubernetes de
production, avec **TLS automatique** (Let's Encrypt), **rolling updates sans
interruption** et **sans perte de données**.

> Source de vérité des noms / ports / clés : [`CONTRACT.md`](./CONTRACT.md).
> Ce document explique *comment opérer* ; le contrat décrit *ce qui est déployé*.

---

## 1. Pré-requis

### Outils (poste d'opération)
- `kubectl` configuré sur le bon contexte (`kubectl config current-context`).
- `docker` (uniquement si vous construisez les images : `start-prod.sh --build` ou `update-prod.sh`).
- `git`, `openssl`, `bash`, `sed`, `base64` (présents sur macOS/Linux).

### Cluster
- Un cluster Kubernetes fonctionnel avec un **LoadBalancer** (IP publique pour l'ingress).
- **ingress-nginx** installé :
  ```bash
  helm upgrade --install ingress-nginx ingress-nginx \
    --repo https://kubernetes.github.io/ingress-nginx \
    --namespace ingress-nginx --create-namespace
  ```
- **cert-manager** installé :
  ```bash
  kubectl apply -f https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml
  ```
  `start-prod.sh` détecte l'absence de l'un ou l'autre et l'indique (il peut continuer
  sur confirmation, mais le TLS / l'Ingress ne fonctionneront pas tant qu'ils ne sont pas là).

### Registre d'images (GHCR)
- Les images sont poussées sur `ghcr.io/lammensmichel/<service>:<TAG>`.
- Il faut un **PAT GitHub** avec la portée `write:packages` (build/push) — `read:packages`
  suffit côté cluster pour le pull, mais le même PAT sert au `imagePullSecret ghcr-creds`.

---

## 2. Configuration (variables)

Renseignez ces variables (en tête des scripts **ou** via l'environnement) :

| Variable | Description | Exemple |
|---|---|---|
| `DOMAIN` | domaine racine | `eshop.example.com` |
| `ACME_EMAIL` | email Let's Encrypt | `admin@example.com` |
| `REGISTRY` | registre (défaut) | `ghcr.io/lammensmichel` |
| `NAMESPACE` | namespace (défaut) | `eshop` |
| `GHCR_USER` | utilisateur GitHub | `lammensmichel` |
| `GHCR_PAT` | PAT GitHub | `ghp_xxx` |
| `TAG` | tag d'image (défaut = sha git court) | `a1b2c3d` |

Exemple :
```bash
export DOMAIN=eshop.example.com
export ACME_EMAIL=admin@example.com
export GHCR_USER=lammensmichel
export GHCR_PAT=ghp_xxxxxxxxxxxxxxxx
```

Rendez les scripts exécutables :
```bash
chmod +x k8s/start-prod.sh k8s/update-prod.sh
```

---

## 3. DNS

Deux sous-domaines doivent pointer vers l'**IP publique de l'ingress-nginx** :

| Hôte | Rôle |
|---|---|
| `app.DOMAIN` | front WebApp + APIs appelées par le navigateur (`/api/...`) |
| `id.DOMAIN` | autorité OIDC Identity (doit être joignable publiquement) |

Récupérez l'IP :
```bash
kubectl get svc -n ingress-nginx
```
Créez deux enregistrements **A** (ou **CNAME**) `app` et `id` vers cette IP.
Le certificat Let's Encrypt (challenge HTTP-01) n'est émis qu'**une fois le DNS résolu**.

---

## 4. Premier déploiement

```bash
# Si les images sont déjà construites/poussées :
./k8s/start-prod.sh

# Pour construire ET pousser les images avant de déployer :
./k8s/start-prod.sh --build
```

`start-prod.sh` est **idempotent** et enchaîne :

1. vérifications (kubectl, docker si `--build`, cert-manager, ingress-nginx, contexte kube + confirmation) ;
2. (option `--build`) build + push des 7 images ;
3. namespace `eshop` ;
4. `imagePullSecret ghcr-creds` (recréé proprement) ;
5. `Secret eshop-secrets` — **génère des mots de passe forts** (`openssl rand`) s'il
   est absent (postgres / redis / rabbitmq + `rabbitmq-user=eshop` + `identity-demo-password`) ;
   s'il existe déjà, il est **conservé tel quel** ;
6. `Secret eshop-connstrings` — composé à partir des mots de passe (chaînes exactes du contrat) ;
7. ConfigMaps (`eshop-config`, `webapp-appsettings`) + `ClusterIssuer letsencrypt-prod`
   (substitution `__DOMAIN__` / `__ACME_EMAIL__`) ;
8. infra (StatefulSets postgres/redis/rabbitmq) + attente `rollout status` ;
9. **Jobs de migration** (catalog / ordering / identity, substitution `__TAG__`) +
   `wait --for=condition=complete` — **échec ⇒ arrêt** (pas de rollout) ;
10. apps (Deployments/Services/PDB/HPA) + Ingress (substitution `__TAG__` / `__DOMAIN__`)
    + `rollout status` par service.

À la fin, l'URL `https://app.DOMAIN` et le rappel DNS sont affichés.

---

## 5. Mises à jour (zéro interruption)

```bash
./k8s/update-prod.sh            # TAG = sha git court courant
./k8s/update-prod.sh a1b2c3d    # ou un TAG explicite
```

Étapes :

1. `TAG` = sha git court (ou 1er argument) ;
2. **build + push** des 7 images vers GHCR
   (`docker build -f src/<svc>/Dockerfile -t $REGISTRY/<svc>:$TAG .` depuis la racine) ;
3. **migrations D'ABORD** : Jobs recréés avec le nouveau TAG (noms suffixés par le TAG
   pour rester uniques/immuables) + `wait --for=condition=complete`.
   **Échec ⇒ arrêt** : aucun rollout, l'état stable est conservé ;
4. **rollout** : `kubectl set image deployment/<svc> ...` pour les 7 services, puis
   `kubectl rollout status` ;
5. nettoyage optionnel des anciens Jobs de migration terminés.

### Mapping service → Dockerfile
| service | Dockerfile |
|---|---|
| `catalog-api` | `src/Catalog.API/Dockerfile` |
| `basket-api` | `src/Basket.API/Dockerfile` |
| `ordering-api` | `src/Ordering.API/Dockerfile` |
| `identity-api` | `src/Identity.API/Dockerfile` |
| `webapp` | `src/WebApp.Server/Dockerfile` |
| `orderprocessor` | `src/OrderProcessor/Dockerfile` |
| `paymentprocessor` | `src/PaymentProcessor/Dockerfile` |

Le **contexte de build est la racine du repo** (les Dockerfiles référencent plusieurs projets).

---

## 6. Stratégie zéro-interruption / zéro perte de données

**Zéro interruption (rolling update)** — garanti par les manifests des Deployments :
- `strategy.rollingUpdate.maxUnavailable: 0` + `maxSurge: 1` : Kubernetes crée d'abord
  un nouveau pod, attend qu'il soit **Ready**, puis seulement retire un ancien pod ;
- `minReadySeconds: 10`, `readinessProbe` (`/health`), `livenessProbe`/`startupProbe`
  (`/alive`) : un pod ne reçoit du trafic que sain ;
- `PodDisruptionBudget minAvailable: 1` : protège pendant les opérations de drain ;
- `terminationGracePeriodSeconds: 30` : drainage propre des connexions.

**Zéro perte de données** :
- **Migrations en Job, AVANT le rollout, backward-compatibles** : le schéma est migré
  pendant que l'ancienne version tourne encore ; les changements doivent rester
  compatibles avec l'ancien code (ajouts de colonnes nullable, pas de DROP destructif
  dans la même release). Si une migration échoue, le rollout n'a pas lieu.
- **Persistance** : Postgres (PVC 5Gi), Redis (AOF, PVC 1Gi), RabbitMQ (PVC 1Gi, queues durables).
- Les workers (`orderprocessor`/`paymentprocessor`) sont **idempotents** côté consumer
  (ack manuel / requeue sur échec), donc tolérants au redémarrage pendant un rollout.

---

## 7. Ports & sondes (rappel)

- Tous les pods applicatifs écoutent en HTTP sur le port **8080** (`ASPNETCORE_URLS=http://+:8080`).
- Sondes (port 8080) : `readinessProbe GET /health`, `livenessProbe GET /alive`,
  `startupProbe GET /alive`.
- **`/health` et `/alive` NE SONT PAS exposés publiquement** par l'Ingress
  (usage interne au cluster uniquement). Le management RabbitMQ (15672) et les
  workers ne sont pas exposés non plus.
- Ingress public :
  - `app.DOMAIN` : `/api/catalog`→catalog-api, `/api/basket`→basket-api,
    `/api/orders`→ordering-api, `/`→webapp (TLS `app-tls`) ;
  - `id.DOMAIN` : `/`→identity-api (TLS `id-tls`).

---

## 8. Sauvegardes Postgres & restauration

Un **CronJob `postgres-backup`** s'exécute chaque nuit et écrit un dump (`pg_dumpall`
ou `pg_dump` des 3 bases) dans le PVC dédié **`postgres-backups`** (5Gi), avec rétention
de N jours.

**Lister les sauvegardes :**
```bash
# Démarrer un pod temporaire qui monte le PVC postgres-backups, ou exec dans un pod outil
kubectl -n eshop get cronjob postgres-backup
kubectl -n eshop get pods -l job-name   # pods de backup terminés
```

**Restaurer une base** (exemple, depuis un dump présent dans le PVC, via un pod psql) :
```bash
# Copier/atteindre le fichier de dump, puis :
kubectl -n eshop exec -i statefulset/postgres -- \
  psql -U postgres -d catalogdb < dump-catalogdb.sql
# pour un pg_dumpall complet :
kubectl -n eshop exec -i statefulset/postgres -- psql -U postgres < dumpall.sql
```
> Restaurer sur une base existante peut nécessiter de la recréer au préalable
> (`DROP DATABASE` / `CREATE DATABASE`) selon le contenu du dump. Coupez les apps
> qui écrivent dans la base ciblée pendant une restauration complète.

---

## 9. Créer un administrateur

Les utilisateurs de démo (`alice` = Admin+Customer, `bob` = Customer) ne sont **pas**
seedés en prod par défaut (`Identity__SeedDemoUsers="false"` dans `eshop-config`).

Pour seeder les comptes de démo (dev/staging) :
- mettez `Identity__SeedDemoUsers="true"` dans la ConfigMap `eshop-config`, et
- définissez le mot de passe via le Secret `eshop-secrets` clé `identity-demo-password`
  (consommé en `Identity__DemoPassword`), puis
- relancez le **Job de migration identity** (qui prend en charge le seed) :
  ```bash
  kubectl -n eshop create job --from=cronjob/... # (ou re-appliquer migrate-identity du release courant)
  ```
  En pratique, `update-prod.sh` recrée `migrate-identity-<TAG>` à chaque release ; activez
  le flag avant de relancer une mise à jour.

Le mot de passe de démo généré se lit ainsi :
```bash
kubectl -n eshop get secret eshop-secrets \
  -o jsonpath='{.data.identity-demo-password}' | base64 -d; echo
```

> En production réelle, préférez créer l'admin via le parcours d'inscription Identity
> puis lui attribuer le rôle `Admin` plutôt que de laisser le seed de démo actif.

---

## 10. Rollback

Par service (les migrations étant backward-compatibles, revenir à l'image précédente est sûr) :
```bash
kubectl rollout undo deployment/<svc> -n eshop
# état d'un rollout :
kubectl rollout status deployment/<svc> -n eshop
# historique :
kubectl rollout history deployment/<svc> -n eshop
```
Services concernés : `catalog-api`, `basket-api`, `ordering-api`, `identity-api`,
`webapp`, `orderprocessor`, `paymentprocessor`.

> Le rollback rétablit l'**image** précédente, pas le schéma de base. C'est précisément
> pourquoi les migrations doivent rester backward-compatibles : l'ancienne image doit
> savoir fonctionner sur le nouveau schéma.

---

## 11. Dépannage rapide

```bash
kubectl -n eshop get pods                        # état des pods
kubectl -n eshop describe pod <pod>              # events (pull image, probes...)
kubectl -n eshop logs deployment/<svc>           # logs applicatifs
kubectl -n eshop get ingress                     # hosts / adresses
kubectl -n eshop get certificate                 # émission TLS (cert-manager)
kubectl -n eshop describe certificate app-tls    # détails du challenge ACME
```
- **ImagePullBackOff** → vérifier `ghcr-creds` et que l'image `:$TAG` est bien poussée.
- **Certificat non émis** → DNS pas encore résolu vers l'ingress (HTTP-01 échoue).
- **Issuer OIDC incohérent** → `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` doit être actif
  sur identity-api (déjà prévu par le manifest) pour que l'autorité émise = `https://id.DOMAIN`.
