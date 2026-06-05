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
// Politique restreinte aux origines lues UNIQUEMENT en configuration (clé "Cors:AllowedOrigins").
// POURQUOI : plus aucune origine localhost codée en dur ici, pour ne pas qu'une URL de dev
//   serve de repli en production.
//   - DEV LOCAL INCHANGÉ : les origines localhost sont fournies par appsettings.Development.json
//     (clé "Cors:AllowedOrigins") -> comportement identique à avant.
//   - PROD : l'origine est fournie par variable d'environnement / config ; si elle est absente
//     ET qu'on n'est pas en Development, AUCUNE origine n'est autorisée (deny par défaut).
const string CorsPolicy = "OrderingCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

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

// ----------------------------------------------------------------------------
// MODE MIGRATION « one-shot » (déclenché par l'argument --migrate).
// POURQUOI : en production Kubernetes (déploiement zéro-interruption), les migrations
//   de schéma sont exécutées par un Job K8s qui lance LA MÊME image avec --migrate :
//   le process applique les migrations puis se termine proprement (return => code 0).
//   Les pods applicatifs NE migrent PLUS au démarrage en prod (voir plus bas).
// DEV LOCAL INCHANGÉ : Aspire ne passe pas --migrate, ce bloc est ignoré en dev.
if (args.Contains("--migrate"))
{
    using var migrationScope = app.Services.CreateScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await MigrateWithRetryAsync(migrationDb);
    return; // Fin du process : le Job K8s se termine ici (succès).
}

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
// POURQUOI conditionner : en production K8s, c'est le Job de migration (--migrate) qui
//   applique le schéma, PAS les pods applicatifs (plusieurs replicas migrant en parallèle
//   = course / verrous). On migre donc au démarrage UNIQUEMENT en Development, ou si on
//   force explicitement via la config "RunMigrationsAtStartup".
// DEV LOCAL INCHANGÉ : en Development (Aspire), IsDevelopment() est vrai => on migre au
//   démarrage exactement comme avant.
if (app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("RunMigrationsAtStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await MigrateWithRetryAsync(db);
}

app.MapOrderingApi().RequireAuthorization();

app.Run();

// Applique les migrations EF avec un retry simple. POURQUOI : au démarrage d'un cluster
// (ou d'un Job lancé en même temps que Postgres), la base peut ne pas être ENCORE prête à
// accepter des connexions ; quelques tentatives espacées évitent un échec immédiat.
// DEV LOCAL INCHANGÉ : en dev la base répond vite, le premier essai réussit normalement.
static async Task MigrateWithRetryAsync(OrderingDbContext db)
{
    const int maxAttempts = 5;
    var delay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch when (attempt < maxAttempts)
        {
            // Base probablement pas encore prête : on patiente puis on réessaie.
            await Task.Delay(delay);
        }
    }
}
