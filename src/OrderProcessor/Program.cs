using eShop.IntegrationEvents.Messaging;
using OrderProcessor;

// ============================================================================
// Program.cs — point d'entrée du worker OrderProcessor.
// ----------------------------------------------------------------------------
// RÔLE : composer (« câbler ») et démarrer l'hôte qui héberge le GracePeriodWorker.
// Le cœur du service reste un BackgroundService PUR qui consomme/publie sur RabbitMQ.
//
// CONCEPT — pourquoi WebApplication.CreateBuilder ICI (et plus Host.CreateApplicationBuilder) :
//   CIBLE = Kubernetes en prod. K8s ne sait sonder un pod que via HTTP (liveness/
//   readiness probes). Un Host « générique » sans pipeline HTTP n'expose AUCUN
//   endpoint -> K8s ne pourrait pas savoir si le worker est vivant/prêt, et finirait
//   par le tuer ou ne jamais le router. On passe donc à WebApplication.CreateBuilder
//   UNIQUEMENT pour greffer un mini-serveur HTTP qui sert /health et /alive
//   (via MapDefaultEndpoints de ServiceDefaults). La logique métier (période de grâce)
//   est inchangée : c'est toujours le même BackgroundService qui tourne en fond.
//
// ÉCOUTE / PORT :
//   - DEV (Aspire) : Aspire injecte ASPNETCORE_URLS (port alloué dynamiquement) ;
//   - PROD (K8s)   : on injecte ASPNETCORE_URLS=http://+:8080 via le Deployment, et les
//                    probes ciblent ce port 8080. Aucune URL n'est codée en dur ici.
//
// PLACE DANS LA SAGA : applique la « période de grâce » (fenêtre d'annulation) entre
// la soumission de la commande et sa confirmation côté Ordering.
var builder = WebApplication.CreateBuilder(args);

// Socle transverse partagé par TOUS les services Aspire (voir eShop.ServiceDefaults) :
// OpenTelemetry (logs/métriques/traces), health checks par défaut (dont "self"/live),
// service discovery, résilience HTTP. Une seule ligne pour hériter de toute cette plomberie.
builder.AddServiceDefaults();

// Enregistre le bus d'événements RabbitMQ (IEventBus) dans le conteneur DI.
// La chaîne de connexion "rabbitmq" est INJECTÉE par Aspire dans la configuration
// grâce au WithReference(rabbitmq) déclaré dans l'AppHost : aucune URL en dur.
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetConnectionString("rabbitmq")!);

// État partagé worker <-> health check : expose le drapeau « consommateur démarré ».
// Singleton car une seule instance doit être vue à la fois par le worker (écriture)
// et par le WorkerHealthCheck (lecture). Voir ConsumerState.cs.
builder.Services.AddSingleton<ConsumerState>();

// HEALTH CHECK applicatif de readiness : vérifie (a) la connectivité RabbitMQ ET
// (b) que le worker consomme bien (ConsumerState.IsConsuming). Tag "ready" -> compte
// pour /health (readiness K8s), pas pour /alive (liveness). Le check "self" (tag "live")
// est déjà ajouté par AddServiceDefaults().
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("orderprocessor-worker", tags: ["ready"]);

// Enregistre le worker comme service hébergé : l'hôte appellera son ExecuteAsync au
// démarrage et son StopAsync à l'arrêt.
builder.Services.AddHostedService<GracePeriodWorker>();

// Construit l'application (résout la DI).
var app = builder.Build();

// Expose les endpoints de santé HTTP (/health = readiness, /alive = liveness) que
// les probes Kubernetes interrogeront. C'est tout le but du passage à WebApplication.
app.MapDefaultEndpoints();

// Démarre le serveur HTTP (santé) ET, via le hosting, le BackgroundService. Bloque
// sur la boucle de vie jusqu'à l'arrêt (SIGTERM en K8s -> StopAsync du worker).
app.Run();
