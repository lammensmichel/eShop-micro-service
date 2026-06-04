# Tutoriel — lire ce dépôt pour apprendre DDD, CQRS et les microservices

Ce dépôt est une reconstruction pédagogique d'eShop. **Le code est abondamment commenté
« façon tutoriel »** : chaque fichier important commence par un bloc d'en-tête qui explique
son rôle, le concept qu'il illustre et sa place dans l'ensemble. Ce document te donne **l'ordre
de lecture** pour que tout s'enchaîne logiquement.

> Conseil : ouvre les fichiers dans l'ordre ci-dessous et lis les commentaires d'en-tête en
> premier. Chaque en-tête indique « à lire avant / après » pour te guider de proche en proche.

---

## 0. Vue d'ensemble (10 min)

1. **`readme.md`** — le journal de construction (historique des commandes `dotnet`).
2. **`CLAUDE.md`** — la carte d'architecture : services, flux d'événements, conventions DDD.
3. **`src/eShop.AppHost/AppHost.cs`** — **commence vraiment ici.** C'est la *composition root*
   .NET Aspire : elle déclare tous les services, leurs dépendances (`WithReference`/`WaitFor`)
   et contient le **schéma ASCII complet de la saga**. Tu comprends la topologie avant le détail.
4. **`src/eShop.ServiceDefaults/Extensions.cs`** — le socle commun hérité par tous les services
   (observabilité OpenTelemetry, health checks, résilience, authentification JWT).

Concepts : orchestration Aspire, service discovery, `WithReference` (couplage) vs `WaitFor`
(ordre de démarrage), liveness `/alive` vs readiness `/health`.

---

## 1. Le cœur : DDD + CQRS dans `Ordering.API` (le plus important)

C'est LE service de référence du projet. Lis-le dans cet ordre :

### 1a. Le vocabulaire DDD de base — `Domain/SeedWork/`
- `Entity.cs` — entité (identité + liste de *domain events*).
- `ValueObject.cs` — objet-valeur (égalité par valeur, immuable).
- `IAggregateRoot.cs` — racine d'agrégat (frontière de cohérence/transaction).
- `IDomainEvent.cs` — *domain event* (fait métier interne) vs *integration event* (inter-services).
- `IRepository.cs` — pattern Repository (inversion de dépendance).

### 1b. L'agrégat — `Domain/AggregatesModel/OrderAggregate/`
- `Order.cs` — **le fichier clé** : invariants dans le constructeur, **machine à états**
  (`Submitted → AwaitingValidation → StockConfirmed → Paid → Shipped`, ou `Cancelled`),
  setters privés, levée des *domain events* depuis les méthodes.
- `OrderStatus.cs` (enumeration class), `OrderItem.cs` (entité enfant), `Address.cs` (value object).
- `Domain/Events/` — les *domain events* (faits passés : `OrderPlaced`, `OrderShipped`…).

### 1c. CQRS — `Application/`
- `Commands/CreateOrderCommand.cs` + `CreateOrderCommandHandler.cs` — le côté **écriture**
  (commande → handler → agrégat → repository). MediatR.
- `Commands/CreateOrderCommandValidator.cs` + `Behaviors/ValidationBehavior.cs` — validation
  applicative via un *pipeline behavior* (middleware des requêtes), avant le domaine.
- `Commands/OrderPlacedDomainEventHandler.cs` — comment un *domain event* est **traduit en
  integration event** et déposé dans l'outbox.
- `Commands/OrderTransitionCommands.cs` + `…Handlers.cs` — les transitions (Ship/Cancel + saga).
- `Queries/` — le côté **lecture** du CQRS (court-circuite le domaine, lit directement).

### 1d. Infrastructure — `Infrastructure/`
- `OrderingDbContext.cs` — EF Core comme *Unit of Work* + **dispatch automatique des domain
  events sur `SaveChanges`**.
- `Repositories/OrderRepository.cs` — l'implémentation du repository.
- `Outbox/` — le **pattern Outbox** : l'integration event est persisté dans la **même
  transaction** que la commande, puis publié par un *background service* (garantie « au moins
  une fois »).
- `Idempotency/` — éviter de traiter deux fois le même message.
- `Messaging/RabbitMQConsumer.cs` — le consommateur qui relie tout côté réception (multi-routing-
  keys, idempotence, DLQ, retries).

### 1e. Exposition — `Apis/OrderingApi.cs` puis `Program.cs`
La fine couche HTTP (minimal API, `buyerId` dérivé du token = anti-IDOR) et l'assemblage final.

---

## 2. La messagerie partagée — `eShop.IntegrationEvents`

- `Messaging/IntegrationEvent.cs` — la classe de base (l'`Id` sert de clé d'idempotence).
- `Messaging/IEventBus.cs` — l'abstraction du bus.
- `Messaging/RabbitMQPublisher.cs` — l'implémentation (glossaire RabbitMQ complet : exchange,
  queue, routing key, channel, publisher confirms, `mandatory`).
- `Events/BasketCheckoutEvent.cs` — **contient le schéma global de la chorégraphie saga**, puis
  les 5 autres événements (`…Submitted`, `GracePeriodConfirmed`, `…StockConfirmed`,
  `…PaymentSucceeded/Failed`) dans l'ordre des maillons n°2 → n°5.

---

## 3. Le producteur d'événements — `Basket.API`

Panier en cache **Redis**, et au *checkout* il **publie** `BasketCheckoutEvent` (départ de la saga).
- `Models/*` → `Repositories/RedisBasketRepository.cs` → `Apis/BasketApi.cs` → `Program.cs`.

---

## 4. La saga en action — les workers

Chorégraphie (pas d'orchestrateur central : chacun réagit à un événement et en émet un autre).
- `src/OrderProcessor/GracePeriodWorker.cs` — la **période de grâce** (fenêtre d'annulation),
  worker le plus simple : une entrée, une sortie.
- `src/PaymentProcessor/PaymentWorker.cs` — le **paiement simulé**, illustre une **branche**
  (succès → avancement / échec → compensation). `Payment:AlwaysFail=true` pour tester l'échec.

Cycle complet :
```
Submitted → (grâce, OrderProcessor) → AwaitingValidation → StockConfirmed
   → (paiement, PaymentProcessor) → Paid → Shipped     (ou → Cancelled si échec)
```

---

## 5. Les services « support »

- **`Catalog.API`** — EF Core + minimal API. Modèle **anémique** (à comparer avec l'agrégat riche
  `Order`). Lecture publique, écriture réservée `Admin`.
- **`Identity.API`** — OIDC avec Duende IdentityServer. `Config.cs` explique le flux
  **Authorization Code + PKCE**, les scopes, et pourquoi il n'y a pas d'`aud`.

---

## 6. Le front — `WebApp` (Blazor WebAssembly)

- `Program.cs` — service discovery, clients HTTP + `AuthorizationMessageHandler` (jeton Bearer),
  OIDC côté client, `RolesClaimsPrincipalFactory`.
- `Services/BuyerIdProvider.cs`, `App.razor`, `Layout/*`, puis les `Pages/*` dans l'ordre du
  parcours : `Home → Catalog → Basket → Orders → Admin`.

---

## Comment exécuter et observer

```bash
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj
```
Docker doit tourner (Aspire provisionne Postgres/Redis/RabbitMQ). Le **dashboard Aspire** (URL
affichée au démarrage) permet de voir les logs de chaque service et de **suivre la saga en direct**
(crée une commande, observe `orderprocessor` puis `paymentprocessor`, et la commande passer
jusqu'à `Shipped`). Comptes de démo (mot de passe `Pass123$`) : `alice` (Admin) et `bob` (Customer).
