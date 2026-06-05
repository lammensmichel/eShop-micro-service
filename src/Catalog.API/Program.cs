using Catalog.API.Apis;
using Catalog.API.Data;
using Microsoft.EntityFrameworkCore;

// ============================================================================
// FICHIER : Program.cs  —  le POINT DE DÉMARRAGE et le CÂBLAGE de Catalog.API.
//
// RÔLE : c'est ici que tout se branche. Un Program.cs ASP.NET Core suit toujours
//   trois temps :
//     1) builder.Services.Add...  -> on ENREGISTRE les services dans le conteneur
//        d'injection de dépendances (DI) : DbContext, authentification, CORS...
//     2) app.Use...               -> on construit le PIPELINE HTTP : la suite de
//        middlewares que chaque requête traverse, dans l'ORDRE déclaré.
//     3) app.Map... / app.Run()   -> on branche les endpoints et on démarre.
//
// PLACE DANS L'ENSEMBLE : dernier fichier à lire pour Catalog.API ; il relie
//   le DbContext, le seed et les endpoints vus précédemment, plus la sécurité.
// ============================================================================
var builder = WebApplication.CreateBuilder(args);

// AddServiceDefaults : extension partagée (eShop.ServiceDefaults) qui branche
// OpenTelemetry, health checks et service discovery sur chaque service.
builder.AddServiceDefaults();
// AddDefaultAuthentication : configure la validation du JETON JWT entrant.
//   - JWT = jeton signé que le client présente dans l'en-tête Authorization.
//     Cette API ne CRÉE pas de jetons (c'est le rôle d'Identity.API) ; elle les
//     VÉRIFIE : signature, émetteur (issuer), expiration, puis lit les claims.
//   - L'« autorité » (authority) est l'URL d'Identity.API ; l'issuer inscrit dans
//     le jeton doit correspondre à cette authority, sinon le jeton est rejeté.
//   - À noter (voir Identity.API/Config.cs) : pas d'ApiResource => pas de claim
//     "aud" dans les jetons => la validation est configurée avec
//     ValidateAudience = false (dans eShop.ServiceDefaults).
// Indispensable pour que [Authorize(Roles = "Admin")] des endpoints d'écriture marche.
builder.AddDefaultAuthentication();
// Enregistre le DbContext sur Postgres ; "catalogdb" est le nom logique de la
// ressource fourni par l'AppHost Aspire, qui injecte la chaîne de connexion.
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

// Politique CORS restreinte aux origines du front, lues UNIQUEMENT depuis la configuration
// ("Cors:AllowedOrigins"). POURQUOI : plus aucune origine localhost codée en dur dans le code,
// pour éviter qu'une URL de dev fuite/serve de repli en production.
//   - DEV LOCAL INCHANGÉ : les origines localhost sont désormais fournies par
//     appsettings.Development.json (clé "Cors:AllowedOrigins"), donc le comportement reste identique.
//   - PROD : l'origine est fournie par variable d'environnement / config ; si elle est absente
//     ET qu'on n'est pas en Development, on N'AUTORISE AUCUNE origine (deny par défaut)
//     plutôt qu'un repli localhost trompeur.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// ----------------------------------------------------------------------------
// MODE MIGRATION « one-shot » (déclenché par l'argument --migrate).
// POURQUOI : en production Kubernetes, on veut un déploiement zéro-interruption.
//   Les migrations de schéma sont donc exécutées par un Job K8s qui lance LA MÊME
//   image avec l'argument --migrate : le process applique les migrations (+ le seed),
//   puis se termine proprement (return => code de sortie 0). Les pods applicatifs,
//   eux, NE migrent PLUS au démarrage en prod (voir plus bas).
// DEV LOCAL INCHANGÉ : Aspire ne passe pas --migrate, donc ce bloc est ignoré en dev.
if (args.Contains("--migrate"))
{
    using var migrationScope = app.Services.CreateScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await MigrateWithRetryAsync(migrationDb);
    await CatalogContextSeed.SeedAsync(migrationDb);
    return; // Fin du process : le Job K8s se termine ici (succès).
}

// Ordre du pipeline : CORS, puis authentification (qui suis-je ?), puis
// autorisation (ai-je le droit ?). UseAuthentication doit précéder UseAuthorization.
app.UseCors("FrontendCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

// INITIALISATION DE LA BASE au démarrage. Le DbContext a une durée de vie
// "scoped" (une instance par requête HTTP) ; or ici on est HORS requête, donc on
// ouvre manuellement un scope DI pour en obtenir une instance valide.
//   1) MigrateAsync() applique les migrations EF en attente -> crée/met à jour
//      les tables réelles dans Postgres (pas besoin de "dotnet ef database update").
//   2) SeedAsync() insère le jeu de données initial (idempotent, cf. le fichier).
//
// POURQUOI conditionner : en production K8s, c'est le Job de migration (--migrate)
//   qui applique le schéma, PAS les pods applicatifs (déploiement zéro-interruption :
//   plusieurs replicas qui migreraient en parallèle = course / verrous). On migre donc
//   au démarrage UNIQUEMENT en Development, ou si on force explicitement via la config
//   "RunMigrationsAtStartup".
// DEV LOCAL INCHANGÉ : en Development (Aspire), IsDevelopment() est vrai => on migre et
//   on seed au démarrage exactement comme avant.
if (app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("RunMigrationsAtStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await MigrateWithRetryAsync(db);
    await CatalogContextSeed.SeedAsync(db);
}

// Branche le groupe d'endpoints défini dans Apis/CatalogApi.cs.
app.MapCatalogApi();

app.Run();

// Applique les migrations EF avec un retry simple. POURQUOI : au démarrage d'un cluster
// (ou d'un Job lancé en même temps que Postgres), la base peut ne pas être ENCORE prête
// à accepter des connexions ; quelques tentatives espacées évitent un échec immédiat.
// DEV LOCAL INCHANGÉ : en dev la base répond vite, le premier essai réussit normalement.
static async Task MigrateWithRetryAsync(CatalogDbContext db)
{
    const int maxAttempts = 5;
    var delay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch when (attempt < maxAttempts)
        {
            // Base probablement pas encore prête : on patiente puis on réessaie.
            await Task.Delay(delay);
        }
    }
}