using Identity.API;
using Identity.API.Data;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Identity.API.Services;

// ============================================================================
// FICHIER : Program.cs  —  DÉMARRAGE et CÂBLAGE d'Identity.API.
//
// RÔLE : assembler les DEUX couches qui font ce service :
//   1) ASP.NET Core Identity   -> gestion des COMPTES (utilisateurs, mots de
//      passe hachés, rôles), persistée via EF Core (ApplicationDbContext).
//   2) Duende IdentityServer   -> couche OIDC/OAuth 2.0 qui ÉMET les jetons.
//      Elle s'APPUIE sur la couche Identity ci-dessus pour authentifier
//      réellement l'utilisateur, et sur Config.cs pour savoir qui peut quoi.
//   En clair : Identity sait « ce mot de passe est-il bon ? » ; IdentityServer
//   sait « voici le jeton OIDC à délivrer en conséquence ».
//
// POURQUOI l'ISSUER doit matcher l'AUTHORITY : le jeton émis ici inscrit comme
//   "issuer" l'URL publique de CE service. Les APIs (Catalog/Basket/Ordering)
//   sont configurées avec cette même URL comme "authority" et vérifient que
//   l'issuer du jeton lui correspond. L'AppHost Aspire injecte l'URL via la
//   variable d'env Identity__Url pour que les deux côtés voient EXACTEMENT la
//   même valeur ; sinon, tout jeton serait rejeté à la validation.
//
// PLACE DANS L'ENSEMBLE : dernier fichier à lire pour Identity.API ; il relie
//   Config.cs, le DbContext, le seed et CustomProfileService.
// ============================================================================
var builder = WebApplication.CreateBuilder(args);

// Extensions partagées Aspire (télémétrie, health checks, service discovery).
builder.AddServiceDefaults();
// DbContext Identity sur Postgres ("identitydb" = ressource fournie par l'AppHost).
builder.AddNpgsqlDbContext<ApplicationDbContext>("identitydb");

// Politique CORS restreinte à l'origine du front (lue depuis la configuration "Cors:AllowedOrigins").
// Repli dev raisonnable si la configuration est absente.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:7204", "http://localhost:5274" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

// ASP.NET Core Identity : gestion des utilisateurs et des rôles, stockés via EF Core.
// C'est la couche "gestion des comptes" (mots de passe, connexions, rôles).
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()   // persiste users/rôles dans ApplicationDbContext.
    .AddDefaultTokenProviders();                        // jetons pour reset de mot de passe, confirmation, etc.

// Duende IdentityServer : la couche OpenID Connect / OAuth 2.0 (émission des jetons).
// Elle s'appuie sur Identity ci-dessus pour authentifier réellement les utilisateurs.
builder.Services
    .AddIdentityServer(options =>
    {
        // Activation de tous les évènements (succès/échec/erreur) : utile pour le diagnostic.
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
    })
    // Chargement "en mémoire" de la configuration définie dans Config.cs :
    .AddInMemoryIdentityResources(Config.IdentityResources)  // scopes d'identité (openid, profile, roles...).
    .AddInMemoryApiScopes(Config.ApiScopes)                  // permissions d'accès aux APIs (catalog, basket...).
    // Clients construits à partir de la configuration : l'URL publique du front
    // (Identity:WebAppUrl) n'est plus codée en dur. Dev local INCHANGÉ : la valeur
    // vient d'appsettings.Development.json (https://localhost:7204).
    .AddInMemoryClients(Config.Clients(builder.Configuration)) // applications autorisées (le front "webapp").
    .AddAspNetIdentity<ApplicationUser>()                    // relie IdentityServer au store Identity.
    .AddProfileService<CustomProfileService>();              // injecte les rôles dans les jetons (cf. CustomProfileService).

// Razor Pages : sert les écrans UI (login, logout, erreur) du serveur d'identité.
builder.Services.AddRazorPages();

var app = builder.Build();

// Pipeline HTTP. L'ordre est important :
app.UseStaticFiles();   // sert le CSS/JS des pages d'identité.
app.UseRouting();
// UseCors doit rester avant UseIdentityServer.
app.UseCors("FrontendCors");
app.UseIdentityServer();  // expose les endpoints OIDC (/connect/authorize, /connect/token, discovery...).
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapRazorPages();      // mappe les pages login/logout/erreur.

// ============================================================================
// MIGRATIONS + SEED — deux scénarios distincts selon l'environnement.
//
// POURQUOI distinguer : en prod Kubernetes, on préfère appliquer les migrations
//   via un JOB dédié (conteneur lancé AVANT l'app, avec l'argument "--migrate"),
//   plutôt que de migrer la base au démarrage de CHAQUE réplique (course entre
//   pods, démarrage ralenti). C'est l'approche des autres APIs du système.
//
//   1) Mode "--migrate" : on migre + seed puis on SORT (return), sans démarrer
//      le serveur web. C'est ce que lance le Job Kubernetes.
//   2) Démarrage normal : on ne migre au boot QUE si :
//        - on est en Development (dev local INCHANGÉ : Aspire migre au démarrage), OU
//        - la config "RunMigrationsAtStartup" est explicitement à true.
//      Sinon (prod par défaut) on suppose que le Job "--migrate" l'a déjà fait.
// ============================================================================

// Fonction locale : applique les migrations puis le seed, avec un RETRY simple
// (la base Postgres peut ne pas être prête immédiatement au démarrage du pod).
static async Task MigrateAndSeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<ApplicationDbContext>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Migration");

    // Retry simple : on retente quelques fois si la base n'est pas encore joignable.
    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            logger.LogInformation("Application des migrations (tentative {Attempt}/{Max})...", attempt, maxAttempts);
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations appliquées.");
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "Échec de migration, nouvelle tentative dans 3 s...");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    // UserManager / RoleManager sont nécessaires au seed (hachage du mot de passe, rôles).
    var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
    logger.LogInformation("Démarrage du seed...");
    // Le seed reçoit la configuration : rôles inconditionnels, users démo conditionnels.
    await ApplicationDbContextSeed.SeedAsync(db, userManager, roleManager, configuration);
    logger.LogInformation("Seed terminé.");
}

// Mode 1 — exécution dédiée "--migrate" (Job Kubernetes) : migrer + seed, puis sortir.
if (args.Contains("--migrate"))
{
    await MigrateAndSeedAsync(app.Services);
    return;
}

// Mode 2 — démarrage normal : on ne migre au boot que si Development OU opt-in explicite.
// Dev local INCHANGÉ : IsDevelopment() est vrai => migration + seed comme avant.
if (app.Environment.IsDevelopment()
    || builder.Configuration.GetValue("RunMigrationsAtStartup", false))
{
    await MigrateAndSeedAsync(app.Services);
}

// Endpoint racine minimal : simple "ping" de vérification que le service tourne.
app.MapGet("/", () => "Identity.API is running!");

app.Run();