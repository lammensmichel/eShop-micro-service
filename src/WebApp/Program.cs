using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using WebApp;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var catalogApiUrl = builder.Configuration["services:catalog-api:https:0"]
    ?? "https://localhost:7117";
var basketApiUrl = builder.Configuration["services:basket-api:https:0"]
    ?? "https://localhost:7225";
var orderingApiUrl = builder.Configuration["services:ordering-api:https:0"]
    ?? "https://localhost:7102";
var identityApiUrl = builder.Configuration["services:identity-api:https:0"]
    ?? "https://localhost:7267";

// HttpClients avec token automatique
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

builder.Services.AddOidcAuthentication(options =>
{
    options.ProviderOptions.Authority = identityApiUrl;
    options.ProviderOptions.ClientId = "webapp";
    options.ProviderOptions.ResponseType = "code";
    options.ProviderOptions.DefaultScopes.Add("eshop");
    options.ProviderOptions.DefaultScopes.Add("roles");
    options.AuthenticationPaths.LogOutSucceededPath = "";
});

await builder.Build().RunAsync();