using Catalog.API.Apis;
using Catalog.API.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// AddServiceDefaults : extension partagée (eShop.ServiceDefaults) qui branche
// OpenTelemetry, health checks et service discovery sur chaque service.
builder.AddServiceDefaults();
// AddDefaultAuthentication : configure la validation du jeton JWT (autorité = Identity.API).
// Indispensable pour que [Authorize(Roles = "Admin")] sur les endpoints d'écriture fonctionne.
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

// Au démarrage : on ouvre un scope DI (le DbContext est en durée de vie "scoped"),
// on applique les migrations EF, puis on insère le jeu de données initial.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
    await CatalogContextSeed.SeedAsync(db);
}

// Branche le groupe d'endpoints défini dans Apis/CatalogApi.cs.
app.MapCatalogApi();

app.Run();