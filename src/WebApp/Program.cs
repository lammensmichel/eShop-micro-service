// =============================================================================
// Point d'entrée du client Blazor WebAssembly (WASM).
//
// Ce projet est le FRONT exécuté DANS LE NAVIGATEUR. Il est servi par le projet
// hôte WebApp.Server (un serveur ASP.NET Core minimal qui livre les fichiers
// statiques du WASM). Ce Program.cs-ci s'exécute donc côté client, pas serveur.
//
// Son rôle : configurer l'injection de dépendances du navigateur :
//   - les clients HTTP typés pointant vers chaque microservice (catalog/basket/...),
//   - l'authentification OIDC (OpenID Connect),
//   - les services applicatifs (BuyerIdProvider, etc.).
// =============================================================================

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using WebApp;
using WebApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
// Monte les composants racines sur les éléments HTML de wwwroot/index.html.
// <App> est l'application elle-même ; HeadOutlet permet aux pages de modifier le <head>.
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Résolution des URLs des APIs via le service discovery d'Aspire ---
// Quand le système tourne sous Aspire (AppHost), chaque service est injecté dans
// la configuration sous une clé "services:<nom>:<scheme>:<index>". On lit cette clé
// pour connaître l'adresse réelle du backend, sans la coder en dur.
// La valeur après "??" est un repli (fallback) utile pour un lancement isolé sans Aspire.
var catalogApiUrl = builder.Configuration["services:catalog-api:https:0"]
    ?? "https://localhost:7117";
var basketApiUrl = builder.Configuration["services:basket-api:https:0"]
    ?? "https://localhost:7225";
var orderingApiUrl = builder.Configuration["services:ordering-api:https:0"]
    ?? "https://localhost:7102";
var identityApiUrl = builder.Configuration["services:identity-api:https:0"]
    ?? "https://localhost:7267";

// --- Clients HTTP nommés avec jeton d'accès attaché automatiquement ---
// Pour chaque API protégée, on enregistre un HttpClient nommé. Le point clé est
// AddHttpMessageHandler(AuthorizationMessageHandler) : ce handler intercepte chaque
// requête sortante et y attache le jeton d'accès OIDC (en-tête Authorization: Bearer ...).
// ConfigureHandler précise :
//   - authorizedUrls : les URLs vers lesquelles le jeton peut être envoyé (sécurité :
//     on n'envoie pas le jeton à n'importe qui),
//   - scopes : les scopes OAuth demandés ("eshop") qui doivent figurer dans le jeton.
// Si aucun jeton valide n'est disponible, le handler lève AccessTokenNotAvailableException
// (gérée dans les pages pour rediriger vers la connexion).
builder.Services.AddHttpClient("CatalogAPI", client =>
    client.BaseAddress = new Uri(catalogApiUrl))
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(
            authorizedUrls: new[] { catalogApiUrl },
            scopes: new[] { "eshop" }));

builder.Services.AddHttpClient("BasketAPI", client =>
    client.BaseAddress = new Uri(basketApiUrl))
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(
            authorizedUrls: new[] { basketApiUrl },
            scopes: new[] { "eshop" }));

builder.Services.AddHttpClient("OrderingAPI", client =>
    client.BaseAddress = new Uri(orderingApiUrl))
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(
            authorizedUrls: new[] { orderingApiUrl },
            scopes: new[] { "eshop" }));

// Service partagé fournissant l'identifiant de l'acheteur (le "sub" du jeton).
// Scoped = une instance par utilisateur/contexte de l'app WASM.
builder.Services.AddScoped<BuyerIdProvider>();

// --- Authentification OIDC (Authorization Code Flow + PKCE) ---
// Configure le client WASM comme client OIDC public auprès d'Identity.API.
// Le flux "code" (ResponseType = "code") associé à PKCE est le flux recommandé pour
// les SPA : le navigateur est redirigé vers Identity.API pour se connecter, reçoit un
// code d'autorisation, puis l'échange (avec le verifier PKCE) contre un jeton d'accès.
builder.Services.AddOidcAuthentication(options =>
{
    // Authority = l'émetteur OIDC. Récupéré via service discovery pour que l'issuer
    // vu ici corresponde exactement à celui que les APIs valident côté serveur.
    options.ProviderOptions.Authority = identityApiUrl;
    options.ProviderOptions.ClientId = "webapp";        // identifiant client déclaré dans Identity.API
    options.ProviderOptions.ResponseType = "code";      // Authorization Code Flow (+ PKCE)
    options.ProviderOptions.DefaultScopes.Add("eshop"); // scope d'API requis par les backends
    options.ProviderOptions.DefaultScopes.Add("roles"); // scope qui fait remonter le claim "role"
    options.AuthenticationPaths.LogOutSucceededPath = "";
    // Les rôles sont émis dans le claim "role" -> aligne IsInRole / AuthorizeView Roles.
    options.UserOptions.RoleClaim = "role";
})
// Branche notre factory personnalisée qui éclate le claim "role" (tableau JSON)
// en claims individuels. Voir RolesClaimsPrincipalFactory.cs.
.AddAccountClaimsPrincipalFactory<RolesClaimsPrincipalFactory>();

// Démarre l'application WASM (boucle d'exécution dans le navigateur).
await builder.Build().RunAsync();