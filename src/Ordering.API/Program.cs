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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultAuthentication();
builder.AddNpgsqlDbContext<OrderingDbContext>("orderingdb");

// CORS (point 7) : politique restreinte à l'origine du front lue depuis la configuration
// (clé "Cors:AllowedOrigins"), avec un repli dev raisonnable.
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

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// Validation (point 6) : enregistrement des validateurs FluentValidation
// et du pipeline MediatR de validation.
builder.Services.AddValidatorsFromAssembly(typeof(CreateOrderCommand).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddScoped<IRepository<Order>, OrderRepository>();
builder.Services.AddHostedService<RabbitMQConsumer>();

// Pattern Outbox : bus partagé RabbitMQ + service du journal d'événements d'intégration
// + publisher de fond qui republie de façon fiable les entrées en attente.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);
builder.Services.AddScoped<IIntegrationEventLogService, IntegrationEventLogService>();
builder.Services.AddHostedService<IntegrationEventLogPublisher>();

// Health checks (point 12) : le check de la base est déjà ajouté par AddNpgsqlDbContext ;
// on complète avec un check RabbitMQ.
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMQHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

// Traduit les échecs de validation FluentValidation en réponse 400 (point 6).
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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    if (!await db.Database.CanConnectAsync())
    throw new Exception("Cannot connect to database");

    await db.Database.MigrateAsync();
}

app.MapOrderingApi().RequireAuthorization();

app.Run();
