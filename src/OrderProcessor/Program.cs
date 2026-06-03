using eShop.IntegrationEvents.Messaging;
using OrderProcessor;

// Service de fond pur (pas d'API HTTP) : un Host minimal qui héberge GracePeriodWorker.
// Son seul rôle dans la chorégraphie saga est d'appliquer la « période de grâce » entre
// la soumission de la commande et la confirmation côté Ordering (fenêtre d'annulation).
var builder = Host.CreateApplicationBuilder(args);

// Socle partagé (OpenTelemetry, health checks, service discovery), comme les API.
builder.AddServiceDefaults();

// Bus d'événements RabbitMQ partagé. La chaîne de connexion "rabbitmq" est injectée
// par Aspire (WithReference) — câblage AppHost prévu à l'étape 3.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

builder.Services.AddHostedService<GracePeriodWorker>();

var host = builder.Build();
host.Run();
