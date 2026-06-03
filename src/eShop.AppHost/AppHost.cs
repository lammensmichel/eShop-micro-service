// ============================================================================
// COMPOSITION ROOT ASPIRE (« AppHost »)
// ----------------------------------------------------------------------------
// Ce fichier est le point d'entrée de l'orchestration. À l'exécution
// (`dotnet run --project eShop.AppHost`), Aspire lit ce modèle d'application
// distribuée, démarre les conteneurs d'infrastructure (Postgres, Redis,
// RabbitMQ) et lance chaque projet .NET déclaré ci-dessous, en respectant les
// dépendances exprimées par WaitFor. Le tableau de bord Aspire (URL affichée
// dans la console) agrège logs, traces, métriques et expose les UI de gestion.
//
// Deux briques clés à retenir :
//   - WithReference(resource) : INJECTE la configuration de connexion de la
//     ressource cible dans le service (chaîne de connexion Postgres/RabbitMQ,
//     URL de service-discovery pour les projets...). C'est le couplage logique.
//   - WaitFor(resource) : retarde le DÉMARRAGE du service tant que la ressource
//     n'est pas saine (healthy). C'est l'ordonnancement du démarrage.
// Les deux sont complémentaires : référencer ne suffit pas si la dépendance
// n'est pas encore prête à accepter des connexions.
// ============================================================================
var builder = DistributedApplication.CreateBuilder(args);

// --- Ressources d'infrastructure (conteneurs gérés par Aspire) ---------------

// Serveur Postgres partagé. WithPgAdmin ajoute un conteneur pgAdmin (UI web)
// relié à ce serveur, accessible depuis le tableau de bord Aspire.
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

// Cache Redis (panier). WithRedisInsight ajoute l'UI RedisInsight.
var redis = builder.AddRedis("redis")
    .WithRedisInsight();

// Broker RabbitMQ : colonne vertébrale de la communication asynchrone entre
// services (événements d'intégration). WithManagementPlugin active la console
// de gestion RabbitMQ (exchanges, queues, messages) pour l'observation.
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// Trois bases logiques distinctes sur le même serveur Postgres : chaque service
// possède SA base (un schéma par bounded context), conformément au principe
// « database per service » des microservices.
var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");
var identityDb = postgres.AddDatabase("identitydb");

// --- Projets applicatifs (services .NET) -------------------------------------

// Identity est déclaré en premier : son endpoint HTTPS est injecté dans les autres
// APIs (Identity__Url) pour la validation des jetons JWT.
// WithReference(identityDb) : injecte la chaîne de connexion vers la base identitydb.
// WaitFor(identityDb)       : attend que la base soit prête avant de lancer le service.
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb);

// Référence forte vers l'endpoint HTTPS d'Identity. Sa valeur réelle (host:port
// alloué dynamiquement) n'est connue qu'au démarrage ; Aspire la résout et la
// propage via la variable d'environnement injectée plus bas. C'est ce qui garantit
// que l'« authority » validée par les APIs == l'« issuer » des jetons émis par
// Identity (sinon les jetons seraient rejetés).
var identityEndpoint = identityApi.GetEndpoint("https");

// Catalog : dépend uniquement de sa base. On lui injecte Identity__Url pour pouvoir
// protéger l'écriture (POST réservé au rôle Admin) ; les lectures restent publiques.
var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(catalogDb)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(catalogDb);

// Basket : panier en Redis + publie BasketCheckoutEvent sur RabbitMQ au checkout.
// D'où la double référence (redis + rabbitmq) et la double attente.
var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

// Ordering : cœur DDD/CQRS. Persiste dans orderingdb et consomme/publie sur RabbitMQ
// (consumer BackgroundService + publication des événements de la saga).
var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(orderingDb)
    .WithReference(rabbitmq)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(orderingDb)
    .WaitFor(rabbitmq);

// --- Workers de la chorégraphie saga (Chantier B) ----------------------------
// Ce sont des services sans API HTTP : de simples BackgroundService qui CONSOMMENT
// un événement sur RabbitMQ, font un travail, puis PUBLIENT l'événement suivant.
// Aucune base de données, aucun Identity : leur seule dépendance est le broker, d'où
// la simple paire WithReference(rabbitmq)/WaitFor(rabbitmq).
//
// Place de ces deux workers dans la saga « commande » (chorégraphie, pas d'orchestrateur
// central — chacun réagit à un événement et en émet un autre) :
//   1. Ordering publie OrderStatusChangedToSubmitted
//   2. OrderProcessor   : attend la période de grâce  -> publie GracePeriodConfirmed
//   3. Ordering valide le stock                        -> publie OrderStockConfirmed
//   4. PaymentProcessor : simule le paiement           -> publie Payment(Succeeded|Failed)
//   5. Ordering met à jour le statut de la commande en conséquence
// Chaque flèche est un message sur l'exchange direct "eshop_event_bus", routé par
// sa routing key. Les workers étant idempotents/rejouables, l'ordre de démarrage
// entre eux n'a pas d'importance (seul RabbitMQ doit être prêt).
builder.AddProject<Projects.OrderProcessor>("orderprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.PaymentProcessor>("paymentprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// WebApp (front Blazor, hébergé par WebApp.Server). Référence les 4 APIs : Aspire
// injecte leurs URLs sous forme de clés de service-discovery (services:<nom>:https:0)
// que le client WASM résout au runtime — aucune URL n'est codée en dur.
// On n'attend (WaitFor) que catalog + identity : indispensables au premier rendu
// (afficher le catalogue, se connecter) ; panier/commande peuvent venir ensuite.
var webApp = builder.AddProject<Projects.WebApp_Server>("webapp")
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithReference(orderingApi)
    .WithReference(identityApi)
    .WaitFor(catalogApi)
    .WaitFor(identityApi);

// Construit le modèle d'application distribuée et lance l'orchestration.
builder.Build().Run();