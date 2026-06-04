using eShop.IntegrationEvents.Messaging;
using PaymentProcessor;
using PaymentProcessor.Payment;

// ============================================================================
// Program.cs — point d'entrée du worker PaymentProcessor.
// ----------------------------------------------------------------------------
// RÔLE : composer et démarrer un Host minimal qui héberge le PaymentWorker.
// Service de fond PUR (pas d'API HTTP) : un BackgroundService qui consomme/publie
// sur RabbitMQ. Structure identique à OrderProcessor/Program.cs (même socle).
//
// CONCEPT — Host.CreateApplicationBuilder (et non WebApplication.CreateBuilder) :
// pas de pipeline HTTP ici, juste l'injection de dépendances, la configuration et
// la journalisation nécessaires à un worker.
//
// PLACE DANS LA SAGA : simule le paiement une fois le stock confirmé, puis émet
// l'événement de succès ou d'échec qui fait AVANCER (succès) ou COMPENSER (échec ->
// annulation) la commande côté Ordering.
var builder = Host.CreateApplicationBuilder(args);

// Socle transverse partagé par tous les services (voir eShop.ServiceDefaults) :
// OpenTelemetry, health checks, service discovery, résilience HTTP.
builder.AddServiceDefaults();

// Enregistre le bus RabbitMQ (IEventBus) dans la DI. La chaîne de connexion
// "rabbitmq" est injectée par Aspire via WithReference(rabbitmq) — aucune URL en dur.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

// Enregistre la PASSERELLE de paiement (point d'extension). Par défaut, la SIMULATION.
// Singleton : sans état mutable, une seule instance suffit pour tout le worker.
// Pour un VRAI paiement, remplacer par :
//   AddSingleton<IPaymentGateway, StripePaymentGateway>()  (classe à créer)
// — le PaymentWorker, qui dépend de l'interface IPaymentGateway, n'a pas à changer.
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

// Enregistre le worker comme service hébergé (ExecuteAsync au démarrage, StopAsync à l'arrêt).
builder.Services.AddHostedService<PaymentWorker>();

// Construit l'hôte et bloque sur sa boucle de vie jusqu'à l'arrêt.
var host = builder.Build();
host.Run();
