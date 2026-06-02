using Duende.IdentityServer.Models;

namespace Identity.API;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "User roles", new[] { "role" })
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("catalog", "Catalog API"),
        new ApiScope("basket", "Basket API"),
        new ApiScope("ordering", "Ordering API"),
        new ApiScope("eshop", "eShop Full Access")
    ];

    public static IEnumerable<Client> Clients =>
    [
        new Client
        {
            ClientId = "webapp",
            ClientName = "eShop Web App",
            AllowedGrantTypes = GrantTypes.Code,
            RequireClientSecret = false,
            RequirePkce = true,
            RedirectUris = { "https://localhost:7204/authentication/login-callback" },
            PostLogoutRedirectUris = { "https://localhost:7204/authentication/logout-callback" },
            AllowedScopes = { "openid", "profile", "email", "roles", "eshop" },
            AllowedCorsOrigins = { "https://localhost:7204" },
            AlwaysIncludeUserClaimsInIdToken = true
        }
    ];
}