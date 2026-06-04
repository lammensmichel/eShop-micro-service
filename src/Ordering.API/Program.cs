using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Apis;
using Ordering.API.Application.Behaviors;
using Ordering.API.Application.Commands;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;
using Ordering.API.Infrastructure;
using Ordering.API.Infrastructure.Messaging;
using Ordering.API.Infrastructure.Outbox;
using Ordering.API.Infrastructure.Repositories;
using eShop.IntegrationEvents.Messaging;

// COMPOSITION ROOT du service : c'est ici qu'on CÂBLE tout. Deux phases :
//   A) configuration des services dans le conteneur d'injection de dépendances (builder.Services) ;
//   B) construction de l'app puis assemblage du pipeline HTTP (app.Use..., app.Map...).
// C'est le bon fichier à lire pour comprendre quels composants existent et comment ils se
// relient ; c'est aussi le seul endroit où l'on déclare explicitement les implémentations
// concrètes (repository, services), respectant l'inversion de dépendance du reste du code.
//
// Bon à savoir sur l'auto-enregistrement MediatR : commandes, requêtes, leurs handlers et
// les handlers de domain events sont découverts automatiquement par scan d'assembly (voir
// AddMediatR plus bas). Ajouter un nouveau handler dans cet assembly suffit ; en revanche
// les repositories et les services d'infrastructure doivent être enregistrés à la main ici.

var builder = WebApplication.CreateBuilder(args);

// ServiceDefaults (projet partagé) : OpenTelemetry, health checks de base, service discovery.
// AddDefaultAuthentication : valide les jetons JWT émis par Identity.API.
// AddNpgsqlDbContext : enregistre OrderingDbContext sur Postgres, AVEC POOLING -> d'où le
// constructeur unique du DbContext et l'injection de IMediator par propriété (voir OrderingDbContext).
builder.AddServiceDefaults();
builder.AddDefaultAuthentication();
builder.AddNpgsqlDbContext<OrderingDbContext>("orderingdb");

// CORS : autorise le front (origine distincte) à appeler cette API depuis le navigateur.
// Politique restreinte aux origines lues en configuration (clé "Cors:AllowedOrigins"),
// avec un repli dev raisonnable.
const string CorsPolicy = "OrderingCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    // Repli pour le développement local : ports réels de WebApp.Server (front qui appelle Ordering.API).
    // À surcharger en configuration via la clé "Cors:AllowedOrigins".
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

// MediatR : scanne l'assembly et enregistre AUTOMATIQUEMENT tous les handlers de commandes,
// de requêtes et de domain events. C'est le bus interne (in-process) du CQRS.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// Validation : on enregistre les validateurs FluentValidation (scan d'assembly) ET le
// ValidationBehavior, branché en pipeline OUVERT (IPipelineBehavior<,>) -> il s'exécute
// avant CHAQUE handler MediatR. Voir Application/Behaviors/ValidationBehavior.cs.
builder.Services.AddValidatorsFromAssembly(typeof(CreateOrderCommand).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Repository d'agrégat (enregistrement EXPLICITE, contrairement aux handlers MediatR).
builder.Services.AddScoped<IRepository<Order>, OrderRepository>();
// Consumer RabbitMQ : background service, point d'entrée asynchrone (réception des events).
builder.Services.AddHostedService<RabbitMQConsumer>();

// Pattern Outbox : bus partagé RabbitMQ + service du journal d'événements d'intégration
// + publisher de fond qui republie de façon fiable les entrées en attente.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);
builder.Services.AddScoped<IIntegrationEventLogService, IntegrationEventLogService>();
builder.Services.AddHostedService<IntegrationEventLogPublisher>();

// Health checks : sondes de disponibilité exposées via MapDefaultEndpoints (ServiceDefaults)
// et utilisées par Aspire. Le check de la base est déjà ajouté par AddNpgsqlDbContext ; on
// complète avec un check RabbitMQ (tag "ready" = readiness).
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

// Middleware qui rattrape la ValidationException levée par ValidationBehavior et la traduit
// en réponse HTTP 400 (problem details), avec les messages groupés par champ.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        var errors = ex.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        await Results.ValidationProblem(errors).ExecuteAsync(context);
    }
});

// Au démarrage, on applique les migrations EF Core en attente (crée/maj le schéma).
// Pratique en dev/démo ; en production on préférerait souvent un step de migration dédié.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    if (!await db.Database.CanConnectAsync())
    throw new Exception("Cannot connect to database");

    await db.Database.MigrateAsync();
}

app.MapOrderingApi().RequireAuthorization();

app.Run();
