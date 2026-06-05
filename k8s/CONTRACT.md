# Contrat de déploiement Kubernetes — eShop (production)

> Source de vérité partagée par tous les manifests et scripts. **Tout doit respecter
> ces noms / ports / clés à la lettre** pour rester cohérent. Cible : prod K8s,
> rolling updates **zéro-interruption**, **zéro perte de données**.

## Conventions générales
- **Namespace** : `eshop` (tout y est déployé).
- **Labels** sur tout : `app.kubernetes.io/part-of: eshop` et `app.kubernetes.io/name: <service>`.
- **Registre d'images** : `ghcr.io/lammensmichel`. Image = `ghcr.io/lammensmichel/<service>:<TAG>`.
  Le pull se fait via le imagePullSecret `ghcr-creds` (créé par le script).
- **Domaine** (placeholder `__DOMAIN__`, ex. `eshop.example.com`) :
  - `app.__DOMAIN__` = front (WebApp) **et** les APIs appelées par le navigateur (routage par chemin).
  - `id.__DOMAIN__` = Identity (autorité OIDC, doit être joignable publiquement).
- **Email Let's Encrypt** : placeholder `__ACME_EMAIL__`.

## Services applicatifs (7) — Deployments
Noms de Service/Deployment = clés ci-dessous. **Tous** écoutent en HTTP sur le port **8080**.

| service | image | type | dépendances |
|---|---|---|---|
| `catalog-api` | catalog-api | API web | postgres(catalogdb) |
| `basket-api` | basket-api | API web | redis, rabbitmq, identity |
| `ordering-api` | ordering-api | API web | postgres(orderingdb), rabbitmq, identity |
| `identity-api` | identity-api | API web | postgres(identitydb) |
| `webapp` | webapp | front (sert le WASM) | (aucune en backend) |
| `orderprocessor` | orderprocessor | worker (a /health) | rabbitmq |
| `paymentprocessor` | paymentprocessor | worker (a /health) | rabbitmq |

### Réglages communs à TOUS les pods applicatifs
- `containerPort: 8080`. Env `ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_ENVIRONMENT=Production`,
  `DOTNET_ENVIRONMENT=Production`.
- **Sondes** (port 8080) : `readinessProbe` GET `/health` ; `livenessProbe` GET `/alive` ;
  `startupProbe` GET `/alive` (failureThreshold élevé pour tolérer un démarrage lent).
- **securityContext** : `runAsNonRoot: true`, `runAsUser: 1654` (utilisateur `app` des images
  dotnet:10 non-root), `allowPrivilegeEscalation: false`, `capabilities.drop: [ALL]`,
  `readOnlyRootFilesystem: true` (monter un emptyDir sur `/tmp` si besoin).
- **resources** : requests `cpu: 100m, memory: 128Mi` ; limits `cpu: 500m, memory: 512Mi`
  (workers : requests memory 96Mi). Ajuster librement, mais toujours définir requests+limits.
- **Rolling update** (zéro-interruption) : `strategy.rollingUpdate.maxUnavailable: 0`,
  `maxSurge: 1`. `minReadySeconds: 10`. `terminationGracePeriodSeconds: 30`.
- **replicas** : 2 pour les APIs et le front ; 1 pour chaque worker (ordering reste idempotent ;
  les workers peuvent être à 2 aussi, l'idempotence consumer protège).
- **PodDisruptionBudget** par service : `minAvailable: 1`.
- **HPA** (HorizontalPodAutoscaler) pour catalog-api, basket-api, ordering-api, webapp :
  min 2, max 5, cible CPU 70%.

### Variables d'environnement par service
Les chaînes de connexion sont injectées depuis le Secret `eshop-connstrings` (voir plus bas).
Les valeurs publiques (domaine, CORS) depuis le ConfigMap `eshop-config`.

- **catalog-api** : `ConnectionStrings__catalogdb`, `Cors__AllowedOrigins__0=https://app.__DOMAIN__`,
  `Identity__Url=https://id.__DOMAIN__`, `Identity__RequireHttpsMetadata=true`,
  `RunMigrationsAtStartup=false`.
- **basket-api** : `ConnectionStrings__redis`, `ConnectionStrings__rabbitmq`,
  `Cors__AllowedOrigins__0=https://app.__DOMAIN__`, `Identity__Url=https://id.__DOMAIN__`,
  `Identity__RequireHttpsMetadata=true`.
- **ordering-api** : `ConnectionStrings__orderingdb`, `ConnectionStrings__rabbitmq`,
  `Cors__AllowedOrigins__0=https://app.__DOMAIN__`, `Identity__Url=https://id.__DOMAIN__`,
  `Identity__RequireHttpsMetadata=true`, `RunMigrationsAtStartup=false`.
- **identity-api** : `ConnectionStrings__identitydb`, `Identity__WebAppUrl=https://app.__DOMAIN__`,
  `Identity__SeedDemoUsers` (depuis ConfigMap, défaut `"false"`),
  `Identity__DemoPassword` (depuis Secret `eshop-secrets` clé `identity-demo-password`),
  `Identity__RequireHttpsMetadata=true`, `RunMigrationsAtStartup=false`,
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (derrière l'Ingress, pour que l'issuer = URL publique).
- **webapp** : aucune dépendance backend. **Monte** le ConfigMap `webapp-appsettings` en
  `/app/wwwroot/appsettings.json` (override) — il contient les URLs publiques pour le WASM :
  `{"Backend":{"CatalogApi":"https://app.__DOMAIN__","BasketApi":"https://app.__DOMAIN__","OrderingApi":"https://app.__DOMAIN__","IdentityAuthority":"https://id.__DOMAIN__"}}`.
  (Le navigateur appelle les APIs via `app.__DOMAIN__/api/...`, routées par l'Ingress.)
- **orderprocessor** / **paymentprocessor** : `ConnectionStrings__rabbitmq`.
  paymentprocessor : `Payment__AlwaysFail=false`.

## Infrastructure (StatefulSets, in-cluster, données persistantes)
| composant | image | service:port | volume | secret |
|---|---|---|---|---|
| `postgres` | postgres:17 | postgres:5432 | PVC 5Gi `/var/lib/postgresql/data` | `eshop-secrets/postgres-password` |
| `redis` | redis:8 | redis:6379 | PVC 1Gi `/data` | `eshop-secrets/redis-password` |
| `rabbitmq` | rabbitmq:4-management | rabbitmq:5672 (+15672) | PVC 1Gi `/var/lib/rabbitmq` | `eshop-secrets/rabbitmq-password` (+ user `rabbitmq-user`) |

- **postgres** : crée **3 bases** `catalogdb`, `orderingdb`, `identitydb` via un script
  monté dans `/docker-entrypoint-initdb.d` (ConfigMap `postgres-initdb`). `POSTGRES_USER=postgres`,
  `POSTGRES_PASSWORD` depuis le secret. PGDATA sous-dossier pour éviter le souci de `lost+found`.
- **redis** : `redis-server --requirepass $(REDIS_PASSWORD) --appendonly yes` (persistance AOF).
- **rabbitmq** : `RABBITMQ_DEFAULT_USER`/`RABBITMQ_DEFAULT_PASS` depuis le secret ; queues durables
  (déjà côté code). PVC pour survivre aux redémarrages.
- Chaque StatefulSet : 1 réplica, Service **headless** + Service ClusterIID normal du même nom,
  sondes (pg_isready / redis-cli ping / rabbitmq-diagnostics ping), securityContext non-root quand
  l'image le permet, requests/limits.
- **Sauvegardes Postgres** : `CronJob` `postgres-backup` (toutes les nuits) qui fait un `pg_dumpall`
  (ou `pg_dump` des 3 bases) vers un PVC dédié `postgres-backups` (5Gi). Conserver N jours.
  Documenter la restauration (`psql < dump`).

## Chaînes de connexion (Secret `eshop-connstrings`, généré par le script)
Valeurs (le script les compose à partir des mots de passe du Secret `eshop-secrets`) :
- `ConnectionStrings__catalogdb` = `Host=postgres;Port=5432;Database=catalogdb;Username=postgres;Password=<PG_PWD>`
- `ConnectionStrings__orderingdb` = `Host=postgres;Port=5432;Database=orderingdb;Username=postgres;Password=<PG_PWD>`
- `ConnectionStrings__identitydb` = `Host=postgres;Port=5432;Database=identitydb;Username=postgres;Password=<PG_PWD>`
- `ConnectionStrings__rabbitmq` = `amqp://<RMQ_USER>:<RMQ_PWD>@rabbitmq:5672`
- `ConnectionStrings__redis` = `redis:6379,password=<REDIS_PWD>`

## Secrets / Config
- **Secret `eshop-secrets`** (créé par `start-prod.sh`, JAMAIS commité ; fournir
  `eshop-secrets.example.yaml` comme modèle) : `postgres-password`, `redis-password`,
  `rabbitmq-user`, `rabbitmq-password`, `identity-demo-password`.
- **Secret `eshop-connstrings`** : les chaînes ci-dessus (généré par le script).
- **Secret `ghcr-creds`** (type docker-registry) : pour pull depuis GHCR (créé par le script
  avec un PAT GitHub).
- **ConfigMap `eshop-config`** : `Identity__SeedDemoUsers`, `Identity__RequireHttpsMetadata=true`,
  domaine, origines CORS, etc. (valeurs non secrètes).
- **ConfigMap `webapp-appsettings`** : le `appsettings.json` du WASM (URLs publiques).

## Migrations (Jobs, AVANT le rollout)
Pour chaque base à migrer : un **Job** qui lance l'image du service avec l'argument `--migrate`
(le code supporte ce mode : migre puis s'arrête, code 0). `restartPolicy: Never`, `backoffLimit: 2`.
- `migrate-catalog` : image `catalog-api:<TAG>` args `["--migrate"]` + `ConnectionStrings__catalogdb`.
- `migrate-ordering` : image `ordering-api:<TAG>` args `["--migrate"]` + `ConnectionStrings__orderingdb`.
- `migrate-identity` : image `identity-api:<TAG>` args `["--migrate"]` + `ConnectionStrings__identitydb`
  (+ `Identity__SeedDemoUsers`/`Identity__DemoPassword` si seed voulu).
Les Jobs doivent être recréés à chaque release (nom suffixé par le TAG) et `kubectl wait --for=condition=complete`.

## Ingress + TLS (cert-manager + Let's Encrypt)
- **ClusterIssuer** `letsencrypt-prod` : ACME, `email: __ACME_EMAIL__`, solveur HTTP-01 via
  l'ingress class `nginx`.
- **Ingress** (class `nginx`, annotations cert-manager) :
  - host `app.__DOMAIN__`, TLS secret `app-tls` :
    - `/api/catalog` → `catalog-api:8080`
    - `/api/basket` → `basket-api:8080`
    - `/api/orders` → `ordering-api:8080`
    - `/` → `webapp:8080`
  - host `id.__DOMAIN__`, TLS secret `id-tls` : `/` → `identity-api:8080`.
- Pas d'exposition publique de `/health`, `/alive`, du management RabbitMQ, ni des workers.

## Scripts (dans k8s/)
- **start-prod.sh** : premier déploiement. Idempotent (`kubectl apply`). Étapes : namespace →
  imagePullSecret GHCR → Secret `eshop-secrets` (générer des mots de passe forts si absents) →
  Secret `eshop-connstrings` (composé) → ConfigMaps (substituer `__DOMAIN__`/`__ACME_EMAIL__`) →
  ClusterIssuer → infra (StatefulSets) + `kubectl rollout/wait` → Jobs de migration + `wait complete` →
  apps (Deployments/Services/PDB/HPA) + Ingress → `kubectl rollout status`.
- **update-prod.sh** : mise à jour SANS interruption. Étapes : déterminer `TAG` (sha git court) →
  `docker build`+`push` des 7 images vers GHCR (depuis la racine, `-f src/<svc>/Dockerfile`) →
  Jobs de migration avec le nouveau TAG + `wait complete` (migrations backward-compatibles AVANT
  le rollout) → `kubectl set image`/`apply` des Deployments avec le nouveau TAG →
  `kubectl rollout status` (maxUnavailable=0 garantit zéro-interruption). Rollback documenté
  (`kubectl rollout undo`).
- Variables en tête de script (à renseigner) : `DOMAIN`, `ACME_EMAIL`, `REGISTRY=ghcr.io/lammensmichel`,
  `NAMESPACE=eshop`, `GHCR_USER`, `GHCR_PAT` (ou via env).

## Notes
- Ingress controller supposé : **ingress-nginx**. cert-manager supposé installé (le DEPLOY.md
  indique comment les installer s'ils manquent).
- L'issuer OIDC d'Identity derrière l'Ingress nécessite `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
  pour que l'autorité émise = `https://id.__DOMAIN__` (sinon mismatch d'issuer côté APIs).
