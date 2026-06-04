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

// Politique CORS restreinte à l'origine du front (lue depuis la configuration "Cors:AllowedOrigins").
// Repli dev raisonnable si la configuration est absente.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:7204", "http://localhost:5274" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

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
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
    await CatalogContextSeed.SeedAsync(db);
}

// Branche le groupe d'endpoints défini dans Apis/CatalogApi.cs.
app.MapCatalogApi();

app.Run();