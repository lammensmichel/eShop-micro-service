using Basket.API.Apis;
using Basket.API.Messaging;
using Basket.API.Repositories;
using eShop.IntegrationEvents.Messaging;

// =============================================================================
// FICHIER : Program.cs (Basket.API)
// RÔLE    : point d'entrée et "composition root" du service. C'est ici qu'on
//           assemble toutes les briques vues dans les autres fichiers : auth,
//           cache Redis, bus d'événements, CORS, health checks, endpoints.
// CONCEPT : top-level statements + injection de dépendances + pipeline de middlewares.
//
//   Deux phases distinctes :
//     1) CONFIGURATION DES SERVICES (avant builder.Build()) : on ENREGISTRE les
//        dépendances dans le conteneur DI (qui sait fabriquer quoi).
//     2) PIPELINE HTTP (après builder.Build()) : on ORDONNE les middlewares qui
//        traitent chaque requête. L'ORDRE compte (voir plus bas : auth avant les
//        endpoints, CORS au début, etc.).
//
//   Les "points" (point 3, 7, 12...) renvoient aux étapes du journal de build
//   (readme.md). AddServiceDefaults vient d'eShop.ServiceDefaults (OpenTelemetry,
//   service discovery, health checks de base) ; on N'y touche pas ici.
//
// À LIRE : en DERNIER, une fois compris Models/*, Repositories/*, BasketApi.cs et
//   la lib eShop.IntegrationEvents — ce fichier les relie entre eux.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultAuthentication();
// AddRedisClient : fournit l'IConnectionMultiplexer (singleton, partagé) injecté
// dans RedisBasketRepository, et enregistre AUSSI un health check Redis (point 12).
// "redis" est le nom logique de la ressource déclarée par l'AppHost Aspire.
builder.AddRedisClient("redis");

// Point 7 — CORS restreint : origines lues depuis la configuration
// ("Cors:AllowedOrigins"), avec un repli dev raisonnable (front local).
const string CorsPolicy = "BasketCorsPolicy";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    // Repli dev : origines réelles du front Blazor / WebApp.Server (cf. launchSettings).
    allowedOrigins = ["https://localhost:7204", "http://localhost:5274"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

// Le dépôt de paniers derrière son interface. Singleton : il ne porte aucun état
// par-requête (la connexion Redis sous-jacente est déjà un singleton partagé).
builder.Services.AddSingleton<IBasketRepository, RedisBasketRepository>();

// Point 3 — Bus d'événements RabbitMQ partagé (lib eShop.IntegrationEvents) :
// publisher robuste, connexion partagée et (ré)ouverte de façon asynchrone, un
// channel créé/disposé par publication. Enregistré en singleton derrière IEventBus.
builder.Services.AddRabbitMQEventBus(
    builder.Configuration.GetConnectionString("rabbitmq")!);

// Point 12 — Health check RabbitMQ (Redis est déjà couvert par AddRedisClient).
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

// PIPELINE HTTP — l'ordre est volontaire et important :
app.UseCors(CorsPolicy);          // 1. CORS d'abord (gère les pré-requêtes OPTIONS du navigateur).
app.UseAuthentication();          // 2. Lit/valide le jeton JWT -> remplit le ClaimsPrincipal (d'où vient le buyerId).
app.UseAuthorization();           // 3. Applique les règles d'accès (RequireAuthorization plus bas) APRÈS l'authentification.
app.MapDefaultEndpoints();        // 4. /health & /alive (health checks, dont RabbitMQ/Redis).
app.MapBasketApi()                // 5. Branche les endpoints du panier...
   .RequireAuthorization();       //    ...et exige un jeton valide sur TOUT le groupe (cf. anti-IDOR dans BasketApi).

app.Run();
