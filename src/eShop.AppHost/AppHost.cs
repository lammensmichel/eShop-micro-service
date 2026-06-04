// ============================================================================
// COMPOSITION ROOT ASPIRE (« AppHost ») — À LIRE EN PREMIER
// ----------------------------------------------------------------------------
// QU'EST-CE QUE .NET ASPIRE ? Un framework d'orchestration pour applications
// distribuées en dev : il décrit, démarre et relie l'ensemble des services et
// des dépendances (bases, cache, broker) d'un système, et fournit un tableau de
// bord d'observabilité. On NE déploie pas Aspire en prod ; on s'en sert pour
// faire tourner et inspecter tout le système d'un seul `dotnet run`.
//
// QU'EST-CE QUE CE FICHIER ? Le « composition root » : l'UNIQUE endroit où le
// graphe complet de l'application est assemblé. On y déclare un « modèle
// d'application distribuée » : la liste des RESSOURCES (voir ci-dessous) et leurs
// relations. À l'exécution (`dotnet run --project eShop.AppHost`), Aspire lit ce
// modèle, démarre les conteneurs d'infrastructure (Postgres, Redis, RabbitMQ) et
// lance chaque projet .NET, en respectant l'ordre exprimé par WaitFor.
//
// VOCABULAIRE ASPIRE :
//   - RESSOURCE : tout élément du modèle géré par Aspire — un conteneur (Postgres,
//     Redis, RabbitMQ), une base logique, ou un projet .NET. Les méthodes Add*
//     (AddPostgres, AddRabbitMQ, AddProject<>...) déclarent des ressources.
//   - WithReference(ressource) : INJECTE la config de connexion de la ressource
//     cible dans le service (chaîne de connexion Postgres/RabbitMQ, ou URL de
//     service-discovery pour un projet). C'est le COUPLAGE LOGIQUE : « ce service
//     a besoin de connaître / parler à cette ressource ».
//   - WaitFor(ressource) : retarde le DÉMARRAGE du service tant que la ressource
//     n'est pas saine (healthy, via son health check). C'est l'ORDONNANCEMENT.
//   Les deux sont complémentaires : référencer ne suffit pas si la dépendance
//   n'est pas encore prête à accepter des connexions (d'où souvent les deux ensemble).
//   - SERVICE DISCOVERY : on appelle un service par son NOM logique (ex.
//     "catalog-api") ; Aspire injecte la correspondance nom -> URL réelle (host:port
//     alloués dynamiquement) dans la config du service appelant. Aucune URL en dur.
//
// Le tableau de bord Aspire (URL affichée dans la console au démarrage) agrège
// logs, traces et métriques (alimentés par OpenTelemetry via ServiceDefaults) et
// expose les UI de gestion (pgAdmin, RedisInsight, console RabbitMQ).
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
// Ce sont des services SANS API HTTP : de simples BackgroundService (workers) qui
// CONSOMMENT un événement sur RabbitMQ, font un travail, puis PUBLIENT l'événement
// suivant. POURQUOI ne dépendent-ils QUE de RabbitMQ ? Parce qu'ils ne parlent à
// personne d'autre : pas de base de données (rien à persister), pas d'Identity
// (pas d'API à protéger, ils ne reçoivent aucune requête HTTP authentifiée). Leur
// unique canal d'entrée/sortie est le broker, d'où la simple paire
// WithReference(rabbitmq)/WaitFor(rabbitmq). C'est l'illustration directe du
// couplage par ÉVÉNEMENTS : un worker ignore qui émet ce qu'il consomme et qui
// consommera ce qu'il émet.
//
// CHORÉGRAPHIE (≠ orchestration) : la saga « commande » est une transaction longue
// répartie sur plusieurs services. Ici PAS d'orchestrateur central qui dicterait
// les étapes : chaque service RÉAGIT à un événement reçu et en ÉMET un autre. Le
// scénario complet n'est écrit nulle part — il émerge de la somme des réactions.
//
// SCHÉMA COMPLET DE LA SAGA (chaque flèche = un message sur l'exchange direct
// "eshop_event_bus", routé par sa routing key entre [crochets]) :
//
//   Basket.API
//     └─[basket-checkout]──────────────► Ordering.API (RabbitMQConsumer)
//                                            │ crée l'Order (statut: Submitted)
//                                            ▼
//                          [ordering-order-submitted]
//                                            │
//                                            ▼
//                                     OrderProcessor  ── attend la PÉRIODE DE GRÂCE
//                                            │            (fenêtre d'annulation)
//                          [ordering-grace-period-confirmed]
//                                            │
//                                            ▼
//                                       Ordering.API ── valide le stock
//                                            │
//                          [ordering-order-stock-confirmed]
//                                            │
//                                            ▼
//                                    PaymentProcessor ── simule le paiement
//                                            │
//                         ┌──────────────────┴───────────────────┐
//                  [payment-succeeded]                     [payment-failed]
//                         │                                       │
//                         ▼                                       ▼
//                   Ordering.API                            Ordering.API
//              confirme la commande              COMPENSE : annule la commande
//
// Récapitulatif des étapes :
//   1. Ordering publie OrderStatusChangedToSubmitted  [ordering-order-submitted]
//   2. OrderProcessor   : attend la période de grâce  -> publie GracePeriodConfirmed
//                                                        [ordering-grace-period-confirmed]
//   3. Ordering valide le stock                        -> publie OrderStockConfirmed
//                                                        [ordering-order-stock-confirmed]
//   4. PaymentProcessor : simule le paiement           -> publie Payment(Succeeded|Failed)
//                                                        [payment-succeeded | payment-failed]
//   5. Ordering met à jour le statut de la commande en conséquence (confirme / annule)
//
// Les workers étant idempotents/rejouables (un message redélivré peut être retraité
// sans dommage), l'ordre de démarrage ENTRE eux n'a aucune importance : seul RabbitMQ
// doit être prêt. Un message arrivé avant qu'un worker soit lancé patiente simplement
// dans sa queue durable jusqu'à ce qu'un consommateur se présente.
builder.AddProject<Projects.OrderProcessor>("orderprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.PaymentProcessor>("paymentprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// WebApp (front Blazor, hébergé par WebApp.Server). Référence les 4 APIs : Aspire
// injecte leurs URLs sous forme de clés de service-discovery (services:<nom>:https:0)
// que le client WASM résout au runtime — aucune URL n'est codée en dur.
// POURQUOI WaitFor seulement catalog + identity ? On distingue le COUPLAGE
// (WithReference sur les 4 : le front doit connaître l'URL de chacune) de
// l'ORDONNANCEMENT (WaitFor). Seuls catalog et identity sont indispensables au
// PREMIER rendu (afficher le catalogue, se connecter) : on bloque donc le démarrage
// du front sur eux. Panier et commande n'interviennent que plus tard dans le
// parcours utilisateur ; les attendre retarderait inutilement l'affichage, et le
// service discovery les résoudra de toute façon quand l'utilisateur en aura besoin.
var webApp = builder.AddProject<Projects.WebApp_Server>("webapp")
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithReference(orderingApi)
    .WithReference(identityApi)
    .WaitFor(catalogApi)
    .WaitFor(identityApi);

// Construit le modèle d'application distribuée et lance l'orchestration.
builder.Build().Run();