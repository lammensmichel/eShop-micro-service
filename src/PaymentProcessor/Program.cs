using eShop.IntegrationEvents.Messaging;
using PaymentProcessor;

var builder = Host.CreateApplicationBuilder(args);

// Socle partagé (OpenTelemetry, health checks, service discovery), comme les API.
builder.AddServiceDefaults();

// Bus d'événements RabbitMQ partagé. La chaîne de connexion "rabbitmq" est injectée
// par Aspire (WithReference) — câblage AppHost prévu à l'étape 3.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

builder.Services.AddHostedService<PaymentWorker>();

var host = builder.Build();
host.Run();
