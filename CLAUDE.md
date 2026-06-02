# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A learning rebuild of the .NET `eShop` reference application, recreated incrementally from scratch to study **DDD + CQRS** patterns. `readme.md` is a French build journal — a chronological log of the exact `dotnet` CLI commands used to scaffold each service, package, and migration. Treat it as history, not as setup instructions. There are no test projects.

Target framework is **.NET 10** (`net10.0`). The solution file is `eShop.slnx` (XML-based solution format).

## Commands

```bash
# Run the whole system (starts all services + Postgres/Redis/RabbitMQ containers via Aspire)
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj

dotnet build                                              # build entire solution

# EF Core migrations (dotnet-ef tool must be installed globally)
dotnet ef migrations add <Name> --project src/Ordering.API/Ordering.API.csproj
```

Docker must be running — the AppHost provisions Postgres, Redis, and RabbitMQ as containers. Each API applies its own migrations on startup via `db.Database.MigrateAsync()`, so there is no separate `dotnet ef database update` step in the normal flow. The Aspire dashboard (printed in console on run) is the entry point for inspecting services, logs, and the pgAdmin / RedisInsight / RabbitMQ management UIs.

`readme.md` contains working `curl` examples for the basket → checkout → order flow.

## Architecture

Aspire-orchestrated microservices. `src/eShop.AppHost/AppHost.cs` is the composition root — it declares every service, its backing resources (`WithReference`), and startup ordering (`WaitFor`). Read it first to understand the topology.

**Services:**
- **Catalog.API** — product catalog. EF Core + Postgres (`catalogdb`), seeded on startup.
- **Basket.API** — shopping cart in Redis. On checkout, **publishes** a `BasketCheckoutEvent` to RabbitMQ.
- **Ordering.API** — the DDD/CQRS heart of the project (see below). Postgres (`orderingdb`) + RabbitMQ **consumer**.
- **Identity.API** — Duende IdentityServer + ASP.NET Core Identity over Postgres (`identitydb`).
- **WebApp** — Blazor WebAssembly client, **hosted by** WebApp.Server. The WASM client resolves backend URLs from Aspire service-discovery config keys (e.g. `services:catalog-api:https:0`) injected at runtime.

Cross-service contracts live in **eShop.IntegrationEvents** (shared class library). **eShop.ServiceDefaults** provides `AddServiceDefaults()` / `MapDefaultEndpoints()` (OpenTelemetry, health checks, service discovery) — every service calls these in `Program.cs`.

### Event flow (the cross-service spine)

```
Basket.API  POST /api/basket/checkout
  → RabbitMQPublisher publishes BasketCheckoutEvent
    to direct exchange "eshop_event_bus", routing key "basket-checkout"
      → Ordering.API RabbitMQConsumer (BackgroundService) consumes it
        → maps to CreateOrderCommand, sent via MediatR
          → Order aggregate created + persisted
```

The exchange name (`eshop_event_bus`), routing key (`basket-checkout`), and the `BasketCheckoutEvent` shape must stay in sync between `Basket.API/Messaging/RabbitMQPublisher.cs` and `Ordering.API/Infrastructure/Messaging/RabbitMQConsumer.cs`. The consumer uses manual ack (`BasicAckAsync`) on success and nack-with-requeue on failure.

### DDD / CQRS conventions (Ordering.API)

This service is the reference for the patterns being learned; mirror its structure when extending it.

- **`Domain/SeedWork/`** — base building blocks: `Entity` (carries `Id` + a private domain-event list with `AddDomainEvent` / `ClearDomainEvents`), `ValueObject`, `IAggregateRoot`, `IDomainEvent`, `IRepository<T>`.
- **`Domain/AggregatesModel/OrderAggregate/`** — the `Order` aggregate root. Invariants are enforced in the constructor and state transitions (`Ship`, `Cancel`, `SetAwaitingValidation`); setters are `private`. The aggregate raises domain events from inside its own methods (e.g. constructor raises `OrderPlacedDomainEvent`). `Address` is a value object; `OrderItem` and `OrderStatus` model line items and state.
- **`Application/Commands/`** and **`Application/Queries/`** — CQRS split via **MediatR** (`IRequestHandler`). Commands mutate through the aggregate + repository; queries read directly.
- **Domain event dispatch is manual**: after `SaveChangesAsync()`, the command handler loops `order.DomainEvents`, calls `_mediator.Publish(...)`, then `ClearDomainEvents()`. Domain-event handlers live in `Application/Commands/` (e.g. `OrderPlacedDomainEventHandler`).
- **`Infrastructure/`** — `OrderingDbContext`, `Repositories/OrderRepository` (implements `IRepository<Order>`), and `Messaging/RabbitMQConsumer`.
- **`Apis/OrderingApi.cs`** — minimal-API endpoint group (`MapGroup("/api/orders")`), registered in `Program.cs` via an extension method.

MediatR handlers are auto-registered from the assembly in `Program.cs` (`RegisterServicesFromAssembly`). New commands/queries/handlers in this assembly are picked up automatically; repositories must be registered explicitly in `Program.cs`.

## Authentication

OIDC is wired end-to-end. `eShop.ServiceDefaults` exposes `AddDefaultAuthentication()`, which configures JWT bearer validation; each API calls it in `Program.cs` plus `UseAuthentication()`/`UseAuthorization()`. The Identity authority URL is injected by the AppHost as the `Identity__Url` env var (from `identityApi.GetEndpoint("https")`) so the **issuer** seen by the Blazor front matches the **authority** validated by the APIs. There are no `ApiResource`s (only `ApiScope`s), so tokens carry no `aud` claim → `ValidateAudience = false`.

- Protected: all of `Basket.API` and `Ordering.API` (`.RequireAuthorization()` on the route group). `Catalog.API` reads are public; `POST /api/catalog/items` is `[Authorize(Roles = "Admin")]`.
- Roles ride in the `role` claim. In Blazor WASM, `RolesClaimsPrincipalFactory` (+ `UserOptions.RoleClaim = "role"`) splits the JSON-array `role` claim into individual claims so `AuthorizeView Roles="Admin"` works for multi-role users.
- Seeded users (`Identity.API/Data/ApplicationDbContextSeed.cs`), password `Pass123$`: `alice` = Admin + Customer, `bob` = Customer.

## Notes for working in this repo

- The WebApp uses a shared `BuyerIdProvider` (`src/WebApp/Services/`) as the single source of the `BuyerId` (the OIDC `sub`). Do not hard-code buyer IDs like `"user1"` in pages — earlier that mismatch made the basket appear empty.
- Code comments and the readme are in French; match the surrounding language when editing existing files.
