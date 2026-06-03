using eShop.IntegrationEvents.Messaging;
using PaymentProcessor;

// Service de fond pur (pas d'API HTTP) : un Host minimal qui héberge PaymentWorker.
// Son rôle dans la chorégraphie saga est de simuler le paiement une fois le stock
// confirmé, puis d'émettre l'événement de succès ou d'échec qui fait avancer (ou
// compense) la commande côté Ordering.
var builder = Host.CreateApplicationBuilder(args);

// Socle partagé (OpenTelemetry, health checks, service discovery), comme les API.
builder.AddServiceDefaults();

// Bus d'événements RabbitMQ partagé. La chaîne de connexion "rabbitmq" est injectée
// par Aspire (WithReference) — câblage AppHost prévu à l'étape 3.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

builder.Services.AddHostedService<PaymentWorker>();

var host = builder.Build();
host.Run();
