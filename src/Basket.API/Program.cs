using Basket.API.Apis;
using Basket.API.Messaging;
using Basket.API.Repositories;
using eShop.IntegrationEvents.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultAuthentication();
// AddRedisClient enregistre aussi un health check Redis (point 12).
builder.AddRedisClient("redis");

// Point 7 — CORS restreint : origines lues depuis la configuration
// ("Cors:AllowedOrigins"), avec un repli dev raisonnable (front local).
const string CorsPolicy = "BasketCorsPolicy";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    // Repli dev : origines locales typiques du front Blazor / WebApp.Server.
    allowedOrigins = ["https://localhost:5001", "http://localhost:5000"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

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

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapBasketApi().RequireAuthorization();

app.Run();
