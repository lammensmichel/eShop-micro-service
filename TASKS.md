# Plan d'améliorations — eShop DDD/CQRS

Suivi des 12 améliorations identifiées lors de la revue. Exécution parallélisée par
**agents partitionnés par projet** (chaque agent est seul à modifier ses fichiers → aucune collision).

## Cycle de vie (par lot)

Chaque lot suit un pipeline en 3 étapes (sans barrière entre lots) :

1. **Implémentation** — un agent code les points du lot et compile son projet.
2. **Revue** — un agent relit le `git diff` du lot de façon critique (bugs, régressions,
   respect des décisions transverses, sécurité) et produit des findings.
3. **Correction** — un agent applique les findings de sévérité haute/moyenne.

Puis, en clôture (orchestrateur) : **build complet d'intégration + exécution des tests**.

## Décisions transverses (à respecter par tous les agents)

- **Contrat `buyerId` (point 1)** : le serveur dérive **toujours** le `buyerId` du claim
  `sub` du jeton JWT (`User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier)`).
  Tout `buyerId` fourni par le client (URL/body) est **ignoré**. **Les routes ne changent pas**
  (ex. `GET /api/basket/{buyerId}` reste, mais le paramètre est ignoré côté serveur) → le front
  n'a aucune adaptation à faire. Cela évite tout couplage entre les agents.
- **Ne pas modifier** `eShop.ServiceDefaults`, `eShop.IntegrationEvents`, `eShop.AppHost`
  (sauf mention explicite). Les health checks (point 12) se configurent **par projet**.
- Chaque agent compile **uniquement son projet** (`dotnet build src/<Projet>/<Projet>.csproj`).
  En cas d'erreur de fichier verrouillé (build concurrent), réessayer après quelques secondes.

## Tableau de suivi

| #  | Amélioration | Priorité | Projet(s) | Agent | Statut |
|----|--------------|----------|-----------|-------|--------|
| 1  | `buyerId` dérivé du token (anti-IDOR) | 🔴 | Basket, Ordering | A + B | ✅ |
| 2  | `.gitignore` + retrait clé de signature/bin/obj | 🔴 | racine | (orchestr.) | ✅ |
| 3  | Publisher RabbitMQ thread-safe + reconnexion | 🔴 | Basket | B | ✅ |
| 4  | Idempotence + dead-letter queue (consumer) | 🔴 | Ordering | A | ✅ |
| 5  | Dispatch auto des domain events (SaveChanges) | 🟠 | Ordering | A | ✅ |
| 6  | Validation des commandes (FluentValidation) | 🟠 | Ordering | A | ✅ |
| 7  | CORS restreint (plus de `AllowAll`) | 🟠 | tous les services | A/B/C | ✅ |
| 8  | Tests (domaine Order + handler) | 🟠 | nouveau projet | E | ✅ (11 tests) |
| 9  | Faire vivre le cycle de commande (Ship/Cancel) | 🟡 | Ordering | A | ✅ |
| 10 | Nettoyage DTO/Models morts (front) | 🟡 | WebApp | D | ✅ |
| 11 | Gestion du jeton expiré (redirection login) | 🟡 | WebApp | D | ✅ |
| 12 | Health checks des dépendances (db/redis/mq) | 🟡 | tous les services | A/B/C | ✅ |

## Résultat de la clôture (orchestrateur)

- **Build complet** : ✅ 0 erreur, 0 avertissement.
- **Tests** : ✅ 11/11 réussis (`tests/Ordering.UnitTests`).
- Alignement de la version `Microsoft.EntityFrameworkCore.Relational` (10.0.8) dans le projet de tests.
- Cycle exécuté : 14 agents (implémentation → revue → correction), ~13 min.

### Points de vigilance restants (non bloquants)

- **Migration EF `AddIdempotency`** : appliquée automatiquement au démarrage (`MigrateAsync`).
- **`OrderingDbContext.Mediator`** est assigné via le constructeur de `OrderRepository` : le dispatch
  automatique ne se déclenche que pour les écritures passant par le repository (cas actuel). À garder
  en tête si une écriture directe sur le `DbContext` est ajoutée plus tard.
- **CORS** : les origines de repli (dev) sont codées dans chaque `Program.cs` ; à surcharger via
  la clé de configuration `Cors:AllowedOrigins` (vérifier qu'elles correspondent au port réel du front).
- Validation fonctionnelle de bout en bout (login alice/bob, panier, checkout, commande) à faire en
  lançant la stack Aspire — non réalisable depuis l'environnement de développement de l'agent.

## Répartition des agents

- **Agent A — Ordering.API** : points 1 (Ordering), 4, 5, 6, 7, 9, 12.
- **Agent B — Basket.API** : points 1 (Basket), 3, 7, 12.
- **Agent C — Catalog.API + Identity.API** : points 7, 12.
- **Agent D — WebApp (front Blazor)** : points 10, 11.
- **Agent E — Projet de tests** : point 8.
- **Final (orchestrateur)** : point 2 (`.gitignore` + git), build complet d'intégration, exécution des tests.

## Détail des tâches

### Point 1 — `buyerId` depuis le token
Voir décision transverse. Récupérer `sub` via le `ClaimsPrincipal` injecté dans les handlers
minimal API. Concerne tous les endpoints qui manipulent le panier ou les commandes d'un acheteur.

### Point 3 — Publisher RabbitMQ robuste
Le `IChannel` unique partagé en singleton n'est pas thread-safe. Créer un channel par
publication (ou un pool), gérer la reconnexion si la connexion tombe, supprimer le
`.GetAwaiter().GetResult()` bloquant au démarrage.

### Point 4 — Idempotence + DLQ
Empêcher la création de commandes en double si un `BasketCheckoutEvent` est redélivré
(clé d'idempotence persistée). Configurer une dead-letter queue + limite de retries au lieu
du `nack(requeue:true)` infini (poison message).

### Point 5 — Dispatch automatique des domain events
Intercepter `SaveChangesAsync` du `OrderingDbContext` pour publier les domain events après
commit, puis les nettoyer. Retirer le dispatch manuel dans `CreateOrderCommandHandler`.

### Point 6 — Validation des commandes
FluentValidation + un `ValidationBehavior` (pipeline MediatR) → `400` propre avant le domaine.

### Point 7 — CORS
Remplacer `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` par une politique restreinte
à l'origine du front (lue depuis la configuration), avec `AllowCredentials` si nécessaire.

### Point 8 — Tests
Projet xUnit. Tester les invariants de `Order` (constructeur, items vides, buyerId vide),
les transitions (`Ship`, `Cancel`, `SetAwaitingValidation`) et le calcul `TotalPrice`.

### Point 9 — Cycle de commande
Endpoints pour déclencher les transitions existantes (`SetAwaitingValidation`, `Ship`, `Cancel`)
et lever les domain events associés.

### Point 10 — Nettoyage front
Supprimer le code mort (`WebApp/Models` inutilisés), factoriser les `record` DTO dupliqués
(`BasketDto`, etc.) dans `Models/`.

### Point 11 — Jeton expiré
Gérer `AccessTokenNotAvailableException` dans les pages → `Redirect()` vers la connexion.

### Point 12 — Health checks
Vérifier que les dépendances (PostgreSQL/Redis/RabbitMQ) sont couvertes par des health checks
(les intégrations client Aspire en ajoutent en partie — compléter si besoin).
