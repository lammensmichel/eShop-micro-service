using eShop.IntegrationEvents.Messaging;
using OrderProcessor;

// ============================================================================
// Program.cs — point d'entrée du worker OrderProcessor.
// ----------------------------------------------------------------------------
// RÔLE : composer (« câbler ») et démarrer un Host minimal qui héberge le
// GracePeriodWorker. C'est un service de fond PUR — pas d'API HTTP, pas de port
// web : juste un BackgroundService qui consomme/publie sur RabbitMQ.
//
// CONCEPT — Host.CreateApplicationBuilder vs WebApplication.CreateBuilder :
//   Les API web utilisent WebApplication (pipeline HTTP, middlewares, endpoints).
//   Ici, pas besoin de tout ça : Host.CreateApplicationBuilder donne juste
//   l'injection de dépendances, la configuration et la journalisation — le strict
//   nécessaire pour faire tourner un worker.
//
// PLACE DANS LA SAGA : applique la « période de grâce » (fenêtre d'annulation)
// entre la soumission de la commande et sa confirmation côté Ordering.
var builder = Host.CreateApplicationBuilder(args);

// Socle transverse partagé par TOUS les services Aspire (voir eShop.ServiceDefaults) :
// OpenTelemetry (logs/métriques/traces vers le dashboard), health checks, service
// discovery, résilience HTTP. Une seule ligne pour hériter de toute cette plomberie.
builder.AddServiceDefaults();

// Enregistre le bus d'événements RabbitMQ (IEventBus) dans le conteneur DI.
// La chaîne de connexion "rabbitmq" est INJECTÉE par Aspire dans la configuration
// grâce au WithReference(rabbitmq) déclaré dans l'AppHost : aucune URL en dur.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

// Enregistre le worker comme service hébergé : l'hôte appellera son ExecuteAsync
// au démarrage et son StopAsync à l'arrêt.
builder.Services.AddHostedService<GracePeriodWorker>();

// Construit l'hôte (résout la DI) et bloque sur la boucle de vie jusqu'à l'arrêt.
var host = builder.Build();
host.Run();
