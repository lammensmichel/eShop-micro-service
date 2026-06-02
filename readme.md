# eShop — Apprentissage DDD & CQRS

Reconstruction pédagogique de l'application de référence **.NET eShop**, recréée pas à pas pour
étudier les patterns **Domain-Driven Design (DDD)** et **CQRS**, orchestrée avec **.NET Aspire**.

> Projet d'apprentissage : certaines fonctionnalités sont volontairement incomplètes
> (l'authentification de bout en bout, par exemple). Voir [Limitations connues](#limitations-connues).

---

## Sommaire

- [Architecture](#architecture)
- [Stack technique](#stack-technique)
- [Prérequis](#prérequis)
- [Démarrage rapide](#démarrage-rapide)
- [Flux métier : du panier à la commande](#flux-métier--du-panier-à-la-commande)
- [Structure du projet](#structure-du-projet)
- [DDD & CQRS dans Ordering.API](#ddd--cqrs-dans-orderingapi)
- [Base de données & migrations](#base-de-données--migrations)
- [Tester l'API au curl](#tester-lapi-au-curl)
- [Limitations connues](#limitations-connues)
- [Annexe : journal de scaffolding](#annexe--journal-de-scaffolding)

---

## Architecture

L'application est un ensemble de **microservices** orchestrés par .NET Aspire. Le point d'entrée
unique est `eShop.AppHost`, qui démarre les services et provisionne les conteneurs
(PostgreSQL, Redis, RabbitMQ).

```
                          ┌─────────────────────┐
                          │   WebApp (Blazor     │
                          │   WebAssembly)       │
                          │   + WebApp.Server     │
                          └──────────┬───────────┘
                                     │ HTTP (service discovery)
        ┌────────────────┬───────────┼────────────────┬─────────────────┐
        ▼                ▼           ▼                ▼                 ▼
 ┌────────────┐   ┌────────────┐ ┌────────────┐  ┌─────────────┐ ┌────────────┐
 │ Catalog.API│   │ Basket.API │ │Ordering.API│  │ Identity.API│ │            │
 │  Postgres  │   │   Redis    │ │  Postgres  │  │  Postgres   │ │            │
 └────────────┘   └─────┬──────┘ └─────▲──────┘  └─────────────┘ └────────────┘
                        │              │
                        │  RabbitMQ    │
                        └──────────────┘
                  BasketCheckoutEvent (event bus)
```

| Service          | Rôle                                              | Stockage / Bus            |
|------------------|---------------------------------------------------|---------------------------|
| **Catalog.API**  | Catalogue produits (lecture seule côté client)    | PostgreSQL (`catalogdb`)  |
| **Basket.API**   | Panier d'achat, **publie** l'événement de checkout| Redis + RabbitMQ          |
| **Ordering.API** | Commandes — cœur **DDD/CQRS**, **consomme** l'événement | PostgreSQL (`orderingdb`) + RabbitMQ |
| **Identity.API** | Authentification OIDC (Duende IdentityServer)     | PostgreSQL (`identitydb`) |
| **WebApp**       | Front-end Blazor WebAssembly                      | —                         |
| **WebApp.Server**| Héberge le client WASM et sert `index.html`       | —                         |

Projets transverses :

- **eShop.IntegrationEvents** — contrats d'événements partagés entre services (ex. `BasketCheckoutEvent`).
- **eShop.ServiceDefaults** — configuration commune (OpenTelemetry, health checks, service discovery)
  via `AddServiceDefaults()` / `MapDefaultEndpoints()`.

---

## Stack technique

- **.NET 10** (`net10.0`)
- **.NET Aspire** — orchestration et service discovery
- **PostgreSQL** + **Entity Framework Core** (Npgsql)
- **Redis** — stockage du panier
- **RabbitMQ** — bus d'événements (exchange `eshop_event_bus`, type *direct*)
- **MediatR** — séparation commandes / requêtes (CQRS) et dispatch des domain events
- **Duende IdentityServer** + ASP.NET Core Identity — authentification OIDC
- **Blazor WebAssembly** — interface utilisateur

---

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (démarré) — Aspire provisionne Postgres, Redis et RabbitMQ en conteneurs
- Outil EF Core (pour gérer les migrations) :
  ```bash
  dotnet tool install --global dotnet-ef
  ```

---

## Démarrage rapide

```bash
# 1. Restaurer + compiler
dotnet build

# 2. Lancer toute la stack (services + conteneurs) via Aspire
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj
```

Le **dashboard Aspire** s'ouvre dans le navigateur (URL affichée dans la console). Il centralise :

- l'état et les logs de chaque service,
- les interfaces d'administration : **pgAdmin** (Postgres), **RedisInsight** (Redis),
  **RabbitMQ Management**.

Chaque API applique automatiquement ses migrations EF Core au démarrage
(`Database.MigrateAsync()`) — pas de `dotnet ef database update` manuel en temps normal.

---

## Flux métier : du panier à la commande

C'est la colonne vertébrale du projet, illustrant la communication **asynchrone par événements**
entre microservices :

```
WebApp                Basket.API                 RabbitMQ                 Ordering.API
  │                       │                          │                         │
  │  POST /api/basket     │                          │                         │
  ├──────────────────────►│ (panier en Redis)        │                         │
  │                       │                          │                         │
  │  POST /checkout       │                          │                         │
  ├──────────────────────►│  publie                  │                         │
  │                       │  BasketCheckoutEvent      │                         │
  │                       ├─────────────────────────►│  routing key            │
  │                       │  (exchange eshop_event_bus│  "basket-checkout"      │
  │                       │   + suppression du panier)├────────────────────────►│ RabbitMQConsumer
  │                       │                          │                         │ → CreateOrderCommand
  │                       │                          │                         │   (MediatR)
  │                       │                          │                         │ → agrégat Order
  │                       │                          │                         │   persisté
```

Le nom de l'exchange (`eshop_event_bus`), la routing key (`basket-checkout`) et la forme de
`BasketCheckoutEvent` doivent rester synchronisés entre `Basket.API/Messaging/RabbitMQPublisher.cs`
et `Ordering.API/Infrastructure/Messaging/RabbitMQConsumer.cs`. Le consommateur utilise un
**ack manuel** (succès) / **nack avec requeue** (erreur).

---

## Structure du projet

```
src/
├── eShop.AppHost/            # Orchestration Aspire (point d'entrée — AppHost.cs)
├── eShop.ServiceDefaults/    # Config commune (télémétrie, health, discovery)
├── eShop.IntegrationEvents/  # Contrats d'événements partagés
├── Catalog.API/              # Catalogue — EF Core + Postgres
├── Basket.API/               # Panier — Redis + publisher RabbitMQ
├── Ordering.API/             # Commandes — DDD/CQRS (voir ci-dessous)
├── Identity.API/             # OIDC — Duende IdentityServer
├── WebApp/                   # Front Blazor WebAssembly
└── WebApp.Server/            # Hôte du client WASM
```

---

## DDD & CQRS dans Ordering.API

`Ordering.API` est le service de référence pour les patterns étudiés. Structure interne :

```
Ordering.API/
├── Domain/
│   ├── SeedWork/                    # Briques de base : Entity, ValueObject,
│   │                                #   IAggregateRoot, IRepository, IDomainEvent
│   ├── AggregatesModel/
│   │   └── OrderAggregate/          # Agrégat Order (racine), OrderItem,
│   │                                #   Address (value object), OrderStatus
│   └── Events/                      # OrderPlacedDomainEvent
├── Application/
│   ├── Commands/                    # Écritures (CQRS) + handlers de domain events
│   └── Queries/                     # Lectures (CQRS)
├── Infrastructure/
│   ├── OrderingDbContext.cs
│   ├── Repositories/                # OrderRepository : IRepository<Order>
│   └── Messaging/                   # RabbitMQConsumer (BackgroundService)
└── Apis/OrderingApi.cs              # Endpoints minimal API (/api/orders)
```

Principes appliqués :

- **Agrégat protégé** : les setters de `Order` sont `private`. Les invariants sont garantis dans
  le constructeur et les transitions d'état (`Ship`, `Cancel`, `SetAwaitingValidation`).
- **Domain events** : l'agrégat publie ses propres événements (ex. le constructeur lève
  `OrderPlacedDomainEvent`). `Entity` porte la liste d'événements.
- **CQRS via MediatR** : les commandes mutent l'état à travers l'agrégat + le repository ;
  les requêtes lisent directement.
- **Dispatch manuel des domain events** : après `SaveChangesAsync()`, le handler de commande
  parcourt `order.DomainEvents`, appelle `_mediator.Publish(...)`, puis `ClearDomainEvents()`.
- Les handlers MediatR sont **auto-enregistrés** depuis l'assembly dans `Program.cs`
  (`RegisterServicesFromAssembly`). Les repositories, eux, sont enregistrés explicitement.

---

## Base de données & migrations

Les migrations sont versionnées par service. Pour en ajouter une :

```bash
dotnet ef migrations add <NomMigration> --project src/Ordering.API/Ordering.API.csproj
# (idem pour Catalog.API, Identity.API)
```

L'application des migrations est automatique au démarrage de chaque service.

---

## Tester l'API au curl

> Les ports correspondent aux valeurs par défaut ; vérifie ceux affichés dans le dashboard Aspire.
> Depuis le câblage de l'authentification, les endpoints Basket et Ordering exigent un jeton JWT
> (en-tête `Authorization: Bearer <token>`) ; ces exemples renverront `401` sans jeton.

```bash
# Ajouter un article au panier de "user1"
curl -X POST https://localhost:7225/api/basket -k \
  -H "Content-Type: application/json" \
  -d '{
    "buyerId": "user1",
    "items": [
      { "productId": 1, "productName": ".NET Bot Black Sweatshirt", "unitPrice": 19.5, "quantity": 2 }
    ]
  }'

# Passer commande (déclenche BasketCheckoutEvent → création de la commande)
curl -X POST https://localhost:7225/api/basket/checkout -k \
  -H "Content-Type: application/json" \
  -d '{
    "buyerId": "user1",
    "city": "Brussels", "street": "Rue de la Loi 1",
    "country": "Belgium", "zipCode": "1000",
    "cardNumber": "4111111111111111", "cardHolderName": "Michel",
    "cardExpiration": "2027-01-01"
  }'

# Consulter les commandes du buyer
curl -k https://localhost:7102/api/orders/user1

# Vérifier la configuration OIDC d'Identity
# https://localhost:7267/.well-known/openid-configuration
```

---

## Authentification

L'authentification OIDC est câblée de bout en bout :

- **Identity.API** (Duende IdentityServer) émet les jetons. Comptes de démo (mot de passe `Pass123$`) :
  - `alice` — rôles **Admin** + **Customer**
  - `bob` — rôle **Customer**
- **Front Blazor** : connexion OIDC (Authorization Code + PKCE), jeton porté automatiquement
  sur les appels via `AuthorizationMessageHandler`.
- **APIs** : validation du jeton JWT via `AddDefaultAuthentication()` (dans `eShop.ServiceDefaults`).
  L'URL d'Identity est injectée par l'AppHost (`Identity__Url`) pour garantir la cohérence
  *issuer ↔ authority*.
- **Protections** :
  - `Basket.API` et `Ordering.API` : tous les endpoints exigent un utilisateur authentifié.
  - `Catalog.API` : lecture publique, mais l'ajout de produit (`POST /api/catalog/items`) est
    réservé au rôle **Admin**.
- **Rôles côté WASM** : une `RolesClaimsPrincipalFactory` éclate le claim `role` (tableau JSON
  quand l'utilisateur a plusieurs rôles) pour que `AuthorizeView Roles="Admin"` fonctionne
  (ex. le lien **Admin** du menu s'affiche pour `alice`).

## Limitations connues

- **Données de checkout en dur** : l'adresse et la carte de paiement de l'écran panier sont
  codées en dur (démo). À remplacer par un formulaire.

---

## Annexe : journal de scaffolding

Historique des commandes utilisées pour construire le projet pas à pas (référence pédagogique).

### EF Core / PostgreSQL

```bash
dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Catalog.API/Catalog.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.PostgreSql
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/Catalog.API/Catalog.API.csproj
```

### Redis (Basket)

```bash
dotnet new webapi -n Basket.API -o src/Basket.API --no-openapi
dotnet sln add src/Basket.API/Basket.API.csproj
dotnet add src/Basket.API/Basket.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Basket.API/Basket.API.csproj
dotnet add src/Basket.API/Basket.API.csproj package Aspire.StackExchange.Redis
dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.Redis
```

### RabbitMQ + événements partagés

```bash
dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.RabbitMQ
dotnet add src/Basket.API/Basket.API.csproj package RabbitMQ.Client
dotnet new classlib -n eShop.IntegrationEvents -o src/eShop.IntegrationEvents
dotnet sln add src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj
dotnet add src/Basket.API/Basket.API.csproj reference src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj
# Récupérer les identifiants RabbitMQ du conteneur :
docker exec $(docker ps -qf "name=rabbitmq") env | grep RABBITMQ
```

### Ordering (DDD / CQRS)

```bash
dotnet new webapi -n Ordering.API -o src/Ordering.API --no-openapi
dotnet sln add src/Ordering.API/Ordering.API.csproj
dotnet add src/Ordering.API/Ordering.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/Ordering.API/Ordering.API.csproj reference src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Ordering.API/Ordering.API.csproj

dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Ordering.API/Ordering.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/Ordering.API/Ordering.API.csproj package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Ordering.API/Ordering.API.csproj package RabbitMQ.Client
dotnet add src/Ordering.API/Ordering.API.csproj package MediatR

mkdir -p src/Ordering.API/Domain/AggregatesModel/OrderAggregate
mkdir -p src/Ordering.API/Domain/Events
mkdir -p src/Ordering.API/Domain/SeedWork
mkdir -p src/Ordering.API/Infrastructure/Repositories
mkdir -p src/Ordering.API/Application/Commands
mkdir -p src/Ordering.API/Application/Queries

dotnet ef migrations add InitialCreate --project src/Ordering.API/Ordering.API.csproj
```

### Identity (Duende IdentityServer)

```bash
dotnet new webapi -n Identity.API -o src/Identity.API --no-openapi
dotnet sln add src/Identity.API/Identity.API.csproj
dotnet add src/Identity.API/Identity.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Identity.API/Identity.API.csproj

dotnet add src/Identity.API/Identity.API.csproj package Duende.IdentityServer
dotnet add src/Identity.API/Identity.API.csproj package Duende.IdentityServer.AspNetIdentity
dotnet add src/Identity.API/Identity.API.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Identity.API/Identity.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Identity.API/Identity.API.csproj package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Identity.API/Identity.API.csproj package Microsoft.EntityFrameworkCore.Design

# JWT côté APIs (en cours de câblage)
dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Basket.API/Basket.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer

dotnet ef migrations add InitialCreate --project src/Identity.API/Identity.API.csproj
```

### Front Blazor WebAssembly + hôte

```bash
dotnet new blazorwasm -n WebApp -o src/WebApp
dotnet sln add src/WebApp/WebApp.csproj
dotnet add src/WebApp/WebApp.csproj package Microsoft.AspNetCore.Components.WebAssembly.Authentication
dotnet add src/WebApp/WebApp.csproj package Microsoft.Extensions.Http

dotnet new web -n WebApp.Server -o src/WebApp.Server
dotnet sln add src/WebApp.Server/WebApp.Server.csproj
dotnet add src/WebApp.Server/WebApp.Server.csproj reference src/WebApp/WebApp.csproj
dotnet add src/WebApp.Server/WebApp.Server.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/WebApp.Server/WebApp.Server.csproj package Microsoft.AspNetCore.Components.WebAssembly.Server

# L'AppHost référence l'hôte serveur (et non le projet WASM directement)
dotnet remove src/eShop.AppHost/eShop.AppHost.csproj reference src/WebApp/WebApp.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/WebApp.Server/WebApp.Server.csproj
```
