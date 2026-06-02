var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var redis = builder.AddRedis("redis")
    .WithRedisInsight();

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");
var identityDb = postgres.AddDatabase("identitydb");

// Identity est déclaré en premier : son endpoint HTTPS est injecté dans les autres
// APIs (Identity__Url) pour la validation des jetons JWT.
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api")
    .WithReference(identityDb)
    .WaitFor(identityDb);

var identityEndpoint = identityApi.GetEndpoint("https");

var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(catalogDb)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(catalogDb);

var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(orderingDb)
    .WithReference(rabbitmq)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WaitFor(orderingDb)
    .WaitFor(rabbitmq);

// Workers de la chorégraphie saga (Chantier B). Ils ne dépendent que de RabbitMQ :
// OrderProcessor applique la période de grâce, PaymentProcessor simule le paiement.
builder.AddProject<Projects.OrderProcessor>("orderprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.PaymentProcessor>("paymentprocessor")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var webApp = builder.AddProject<Projects.WebApp_Server>("webapp")
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithReference(orderingApi)
    .WithReference(identityApi)
    .WaitFor(catalogApi)
    .WaitFor(identityApi);

builder.Build().Run();