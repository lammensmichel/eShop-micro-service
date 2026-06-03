using Duende.IdentityServer.Models;

namespace Identity.API;

// Configuration "en mémoire" de Duende IdentityServer.
// Trois listes décrivent QUI peut demander QUOI :
//   - IdentityResources : données d'identité regroupées par scope (qui es-tu ?)
//   - ApiScopes         : permissions d'accès aux APIs (à quoi as-tu droit ?)
//   - Clients           : les applications autorisées à demander des jetons.
// Ces objets sont chargés dans Program.cs via AddInMemory* (pratique pour l'apprentissage ;
// en production on les stockerait plutôt en base via le ConfigurationStore).
public static class Config
{
    // IdentityResources : déterminent les claims d'identité présents dans l'ID Token,
    // selon les scopes demandés. Chaque scope OIDC mappe vers un ensemble de claims.
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),   // scope "openid" : obligatoire en OIDC, fournit le claim "sub" (identifiant utilisateur).
        new IdentityResources.Profile(),  // scope "profile" : claims de profil (name, given_name, ...).
        new IdentityResources.Email(),    // scope "email" : claims email / email_verified.
        // Resource d'identité personnalisée : le scope "roles" expose le claim "role".
        // C'est ce qui permet à [Authorize(Roles = "Admin")] de fonctionner côté APIs.
        new IdentityResource("roles", "User roles", new[] { "role" })
    ];

    // ApiScopes : permissions logiques que les jetons d'accès peuvent porter (claim "scope").
    // IMPORTANT : il n'y a PAS d'ApiResource ici, seulement des ApiScopes. Conséquence :
    // les access tokens ne portent AUCUN claim "aud" (audience). C'est pourquoi les APIs
    // configurent ValidateAudience = false côté validation JWT.
    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("catalog", "Catalog API"),
        new ApiScope("basket", "Basket API"),
        new ApiScope("ordering", "Ordering API"),
        new ApiScope("eshop", "eShop Full Access")   // scope "fourre-tout" demandé par le front pour accéder à l'ensemble.
    ];

    // Clients : applications connues d'IdentityServer. Ici un seul client, le front Blazor.
    public static IEnumerable<Client> Clients =>
    [
        new Client
        {
            ClientId = "webapp",
            ClientName = "eShop Web App",
            // Flux OAuth utilisé : Authorization Code. C'est le flux recommandé pour
            // les applications web/SPA modernes (redirection vers la page de login).
            AllowedGrantTypes = GrantTypes.Code,
            // Client "public" (SPA Blazor WASM) : il ne peut pas garder un secret confidentiel,
            // donc on n'exige pas de client secret...
            RequireClientSecret = false,
            // ...mais on EXIGE PKCE, qui sécurise le flux Authorization Code pour les
            // clients publics (protège contre l'interception du code d'autorisation).
            RequirePkce = true,
            // URL de retour après login réussi (doit correspondre exactement à celle envoyée par le front).
            RedirectUris = { "https://localhost:7204/authentication/login-callback" },
            // URL de retour après déconnexion.
            PostLogoutRedirectUris = { "https://localhost:7204/authentication/logout-callback" },
            // Scopes que ce client a le droit de demander (identité + accès "eshop").
            AllowedScopes = { "openid", "profile", "email", "roles", "eshop" },
            // Origine autorisée pour les appels CORS du navigateur vers IdentityServer.
            AllowedCorsOrigins = { "https://localhost:7204" },
            // Inclut systématiquement les claims utilisateur (dont "role") dans l'ID Token,
            // ce qui évite au front un appel supplémentaire au UserInfo endpoint.
            AlwaysIncludeUserClaimsInIdToken = true
        }
    ];
}