// =============================================================================
// Hôte du client Blazor WebAssembly.
//
// Ce projet est un serveur ASP.NET Core volontairement minimal : son seul rôle est
// de SERVIR le front WASM (projet WebApp) au navigateur. Il n'expose aucune logique
// métier ni API : tous les appels métier partent du navigateur directement vers les
// microservices (Catalog/Basket/Ordering/Identity).
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// Branche les défauts partagés d'Aspire (eShop.ServiceDefaults) :
// OpenTelemetry, health checks et, surtout ici, le service discovery dont la config
// est ensuite transmise au WASM via index.html / les clés "services:...".
builder.AddServiceDefaults();

var app = builder.Build();

// Sert les fichiers du framework Blazor WASM (dll/wasm de l'app compilée).
app.UseBlazorFrameworkFiles();
// Sert les fichiers statiques (wwwroot : index.html, css, js, images...).
app.UseStaticFiles();
// Expose les endpoints par défaut d'Aspire (ex. /health, /alive).
app.MapDefaultEndpoints();
// Routage SPA : toute route inconnue retombe sur index.html pour que le routeur
// Blazor (côté client) prenne le relais (deep-linking, rafraîchissement de page).
app.MapFallbackToFile("index.html");

app.Run();