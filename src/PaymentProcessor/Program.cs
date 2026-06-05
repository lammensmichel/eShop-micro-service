using eShop.IntegrationEvents.Messaging;
using PaymentProcessor;
using PaymentProcessor.Payment;

// ============================================================================
// Program.cs — point d'entrée du worker PaymentProcessor.
// ----------------------------------------------------------------------------
// RÔLE : composer et démarrer l'hôte qui héberge le PaymentWorker. Le cœur du
// service reste un BackgroundService PUR qui consomme/publie sur RabbitMQ.
//
// CONCEPT — pourquoi WebApplication.CreateBuilder ICI (et plus Host.CreateApplicationBuilder) :
//   CIBLE = Kubernetes en prod. K8s ne sait sonder un pod que via HTTP (liveness/
//   readiness probes). Un Host « générique » sans pipeline HTTP n'expose AUCUN
//   endpoint -> K8s ne pourrait pas savoir si le worker est vivant/prêt. On passe donc
//   à WebApplication.CreateBuilder UNIQUEMENT pour greffer un mini-serveur HTTP qui sert
//   /health et /alive (via MapDefaultEndpoints de ServiceDefaults). La logique métier
//   (paiement) est inchangée : c'est toujours le même BackgroundService qui tourne en fond.
//
// ÉCOUTE / PORT :
//   - DEV (Aspire) : Aspire injecte ASPNETCORE_URLS (port alloué dynamiquement) ;
//   - PROD (K8s)   : on injecte ASPNETCORE_URLS=http://+:8080 via le Deployment, et les
//                    probes ciblent ce port 8080. Aucune URL n'est codée en dur ici.
//
// PLACE DANS LA SAGA : simule le paiement une fois le stock confirmé, puis émet
// l'événement de succès ou d'échec qui fait AVANCER (succès) ou COMPENSER (échec) la commande.
var builder = WebApplication.CreateBuilder(args);

// Socle transverse partagé par tous les services (voir eShop.ServiceDefaults) :
// OpenTelemetry, health checks par défaut (dont "self"/live), service discovery, résilience HTTP.
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

// État partagé worker <-> health check : expose le drapeau « consommateur démarré ».
// Singleton car une seule instance doit être vue à la fois par le worker (écriture)
// et par le WorkerHealthCheck (lecture). Voir ConsumerState.cs.
builder.Services.AddSingleton<ConsumerState>();

// HEALTH CHECK applicatif de readiness : vérifie (a) la connectivité RabbitMQ ET
// (b) que le worker consomme bien (ConsumerState.IsConsuming). Tag "ready" -> compte
// pour /health (readiness K8s), pas pour /alive (liveness). Le check "self" (tag "live")
// est déjà ajouté par AddServiceDefaults().
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("paymentprocessor-worker", tags: ["ready"]);

// Enregistre le worker comme service hébergé (ExecuteAsync au démarrage, StopAsync à l'arrêt).
builder.Services.AddHostedService<PaymentWorker>();

// Construit l'application (résout la DI).
var app = builder.Build();

// Expose les endpoints de santé HTTP (/health = readiness, /alive = liveness) que
// les probes Kubernetes interrogeront. C'est tout le but du passage à WebApplication.
app.MapDefaultEndpoints();

// Démarre le serveur HTTP (santé) ET, via le hosting, le BackgroundService. Bloque
// sur la boucle de vie jusqu'à l'arrêt (SIGTERM en K8s -> StopAsync du worker).
app.Run();
