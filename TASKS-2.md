# Plan d'évolution — Outbox + Saga (rapprochement de `dotnet/eShop`)

## Statut d'implémentation

| Chantier | Statut | Vérification |
|----------|--------|--------------|
| 0 — EventBus partagé (IntegrationEvent, IEventBus, RabbitMQPublisher, AddRabbitMQEventBus) | ✅ | build complet |
| A — Outbox (IntegrationEventLogEntry, service, background publisher, transaction atomique) | ✅ | build + 5 tests |
| B — Saga (domaine StockConfirmed/Paid, 4 events, consumer multi-keys, OrderProcessor, PaymentProcessor, câblage AppHost) | ✅ | build + tests |

- **Build complet** : 0 erreur / 0 avertissement. **Tests** : 24/24.
- **Validation e2e** (chorégraphie complète sous Aspire/Docker) : **non réalisée** — Docker
  indisponible dans l'environnement de l'agent. Cohérence des routing keys vérifiée statiquement.
- Tâche **0.3** (classe de base `RabbitMQConsumerBase` partagée) **non faite** : chaque worker
  embarque un consumer léger calqué sur celui d'Ordering (duplication assumée).



Deux chantiers pour combler les deux patterns avancés manquants identifiés lors de la
comparaison avec le projet officiel. **B dépend de A** : la saga n'est fiable que si la
publication des événements d'intégration est transactionnelle (outbox).

> Ordre conseillé : **Chantier 0 (socle)** → **Chantier A (Outbox)** → **Chantier B (Saga)**.

## État actuel (rappel)

- `Ordering.API` **consomme** `BasketCheckoutEvent` mais ne **publie aucun** événement d'intégration.
- Le publisher RabbitMQ vit **dans `Basket.API`** (`Messaging/RabbitMQPublisher.cs`), non partagé.
- Idempotence **consumer** déjà en place (table `ProcessedIntegrationEvents`) → couvre la
  réception en double, mais **pas** la perte d'événement entre commit métier et publication.
- Cycle de l'`Order` : `Submitted → AwaitingValidation → Shipped | Cancelled`
  (`OrderStatus` n'a que 4 valeurs ; pas de `StockConfirmed` ni `Paid`).
- Le précédent transactionnel existe déjà : `RabbitMQConsumer.ProcessEventAsync`
  utilise `CreateExecutionStrategy` + `BeginTransactionAsync` — à réutiliser.

---

## Décisions transverses

- **Bus partagé** : extraire le code RabbitMQ (publisher + consumer) hors de `Basket.API` vers
  une bibliothèque partagée afin que `Ordering.API`, `OrderProcessor` et `PaymentProcessor`
  publient/consomment de la même façon (mini-`EventBus` maison, pas besoin d'aller jusqu'à
  l'abstraction complète d'eShop officiel).
- **Base `IIntegrationEvent`** : `Id` (Guid) + `CreationDate` (UTC). `BasketCheckoutEvent`
  l'implémente déjà de fait (`Id`) → l'aligner.
- **Idempotence** : tout nouveau consumer réutilise la table `ProcessedIntegrationEvents`
  (clé = `IntegrationEvent.Id`), exactement comme le consumer actuel.
- **Migrations** : chaque API applique ses migrations au démarrage (`MigrateAsync`) — pattern existant.
- **Ne pas casser** le flux actuel Basket→Ordering tant que le socle n'est pas en place.

---

# Chantier 0 — Socle : EventBus partagé

| #  | Tâche | Projet(s) |
|----|-------|-----------|
| 0.1 | Créer `eShop.EventBus` (ou un dossier `Messaging/` dans `eShop.IntegrationEvents`) : interface `IEventBus.PublishAsync<T>` + base `IntegrationEvent` (`Id`, `CreationDate`) | nouveau / IntegrationEvents |
| 0.2 | Déplacer `RabbitMQPublisher` (thread-safe, déjà robuste) dans cette lib ; `Basket.API` le consomme depuis là | Basket, lib |
| 0.3 | Factoriser la mécanique consumer (exchange/DLQ/retry header/idempotence) en classe de base réutilisable `RabbitMQConsumerBase` | Ordering, lib |
| 0.4 | Enregistrer le bus en DI dans chaque service (`AddRabbitMQEventBus(connectionString)`) | tous |

**Critère de fin** : build vert + flux checkout→commande inchangé fonctionnellement.

---

# Chantier A — Pattern Outbox (Integration Event Log)

But : garantir qu'un événement d'intégration publié par Ordering est **persisté dans la même
transaction** que le changement métier, puis publié de façon fiable (au moins une fois).

| #  | Tâche | Projet(s) | Priorité |
|----|-------|-----------|----------|
| A.1 | Entité `IntegrationEventLogEntry` : `EventId`, `Content` (JSON), `EventTypeName`, `State` (NotPublished/InProgress/Published/Failed), `TimesSent`, `CreationTime`, `TransactionId` | Ordering.API/Infrastructure/Outbox | 🔴 |
| A.2 | Config EF + migration `AddIntegrationEventLog` (table dans `orderingdb`) | Ordering | 🔴 |
| A.3 | `IIntegrationEventLogService` : `SaveEventAsync(evt, transaction)`, `MarkAsInProgress/Published/Failed`, `RetrievePendingEventsToPublishAsync()` | Ordering | 🔴 |
| A.4 | Brancher dans le flux d'écriture : dans la **même** transaction que `SaveChanges` de l'`Order`, enregistrer l'entrée outbox. Réutiliser le pattern `ExecutionStrategy`+`BeginTransaction` déjà présent dans `RabbitMQConsumer.ProcessEventAsync` | Ordering | 🔴 |
| A.5 | Traduire les **domain events → integration events** : les handlers `OrderXxxDomainEventHandler` (aujourd'hui ils ne font que logger) enfilent l'event d'intégration dans l'outbox | Ordering | 🟠 |
| A.6 | `IntegrationEventLogPublisher : BackgroundService` : poll des entrées `NotPublished`, publie via le bus, `MarkAsPublished` ; sur échec `TimesSent++` / `Failed` ; retient l'ordre | Ordering | 🔴 |
| A.7 | Tests unitaires : sérialisation/désérialisation des entrées, transitions d'état du log | tests | 🟠 |

**Note Basket** : `Basket.API` persiste dans **Redis** (pas de transaction relationnelle), donc
l'outbox y est plus délicat. Le laisser **hors scope** (documenter le compromis) ou utiliser
une petite table outbox dédiée si on veut aller jusqu'au bout — non prioritaire.

**Critère de fin** : tuer le process juste après le commit métier et avant publication →
au redémarrage, l'event part quand même (rejouabilité prouvée).

---

# Chantier B — Saga : OrderProcessor + PaymentProcessor

But : faire **vivre le cycle de commande entre services** par chorégraphie d'événements,
au lieu de transitions purement locales.

### Pré-requis modèle de domaine
- Étendre `OrderStatus` : ajouter `StockConfirmed` (5) et `Paid` (6).
- Ajouter les transitions correspondantes dans `Order` (`SetStockConfirmed()`, `SetPaid()`),
  chacune levant son domain event (cohérent avec l'existant `Ship`/`Cancel`).

### Nouveaux événements d'intégration (`eShop.IntegrationEvents/Events`)
- `OrderStatusChangedToSubmittedIntegrationEvent`
- `GracePeriodConfirmedIntegrationEvent`
- `OrderStockConfirmedIntegrationEvent` (+ éventuel `OrderStockRejected`)
- `OrderPaymentSucceededIntegrationEvent` / `OrderPaymentFailedIntegrationEvent`

### Tâches

| #  | Tâche | Projet(s) | Priorité |
|----|-------|-----------|----------|
| B.1 | Ordering publie (via outbox) `OrderStatusChangedToSubmittedIntegrationEvent` à la création | Ordering | 🔴 |
| B.2 | **Nouveau worker `OrderProcessor`** (`BackgroundService`) : implémente la **grace period** — attend N s après soumission ; si la commande n'a pas été annulée, publie `GracePeriodConfirmedIntegrationEvent` | nouveau projet | 🔴 |
| B.3 | Ordering consomme `GracePeriodConfirmed` → `SetAwaitingValidation()` puis déclenche la validation de stock | Ordering | 🟠 |
| B.4 | Validation de stock **simplifiée** : soit auto-confirmée par Ordering, soit un endpoint/consumer minimal côté `Catalog.API` qui répond `OrderStockConfirmed` | Ordering/Catalog | 🟡 |
| B.5 | **Nouveau worker `PaymentProcessor`** : consomme `OrderStockConfirmed`, simule le paiement (succès/échec configurable), publie `OrderPaymentSucceeded`/`Failed` | nouveau projet | 🔴 |
| B.6 | Ordering consomme le résultat paiement → `SetPaid()` (puis `Ship()`) ou `Cancel()` | Ordering | 🔴 |
| B.7 | Déclarer les 2 nouveaux workers + leurs dépendances (RabbitMQ) dans `eShop.AppHost/AppHost.cs` (`WithReference`/`WaitFor`) | AppHost | 🔴 |
| B.8 | Tests : transitions `StockConfirmed`/`Paid` du domaine + un test d'intégration de la chorégraphie (annulation pendant la grace period) | tests | 🟠 |

**Cycle cible** :
```
Submitted → (grace period, OrderProcessor) → AwaitingValidation
  → StockConfirmed → (PaymentProcessor) → Paid → Shipped
                                         ↘ (échec) → Cancelled
```

**Critère de fin** : créer une commande, l'annuler pendant la grace period → elle n'est jamais
payée ; laisser passer la grace period → elle traverse jusqu'à `Paid`/`Shipped` via les workers.

---

## Récapitulatif des dépendances

- **0 → A → B** (strict).
- B réutilise l'outbox de A pour publier de façon fiable, et la table `ProcessedIntegrationEvents`
  existante pour l'idempotence côté chaque nouveau consumer.
- Les deux nouveaux workers (`OrderProcessor`, `PaymentProcessor`) sont des projets `Worker`
  .NET, branchés dans Aspire comme les API actuelles.
