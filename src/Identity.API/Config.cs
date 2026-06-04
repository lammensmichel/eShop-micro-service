using Duende.IdentityServer.Models;

namespace Identity.API;

// ============================================================================
// FICHIER : Config.cs  —  le CŒUR de la configuration OIDC du serveur d'identité.
//
// CONTEXTE / JARGON (à lire posément, tout le reste en découle) :
//   - OIDC (OpenID Connect) = couche d'IDENTITÉ posée sur OAuth 2.0. OAuth 2.0
//     sert à déléguer l'ACCÈS (« cette app a le droit d'appeler telle API ») ;
//     OIDC ajoute l'AUTHENTIFICATION (« voici QUI est l'utilisateur »).
//   - IdentityServer (ici l'implémentation Duende) = le composant qui IMPLÉMENTE
//     OIDC/OAuth : il authentifie l'utilisateur et ÉMET les jetons. C'est
//     l'« autorité » (authority) de confiance pour toutes les APIs du système.
//   - Deux types de jetons émis :
//       * ID Token   : prouve QUI est l'utilisateur (consommé par le front).
//       * Access Token : autorise l'appel aux APIs (présenté en Bearer aux APIs).
//   - Un "scope" = une permission/portée demandée. Deux familles ici :
//       * scopes d'IDENTITÉ (IdentityResources) : décident quels claims d'identité
//         entrent dans l'ID Token (openid, profile, email, roles...).
//       * scopes d'API (ApiScopes) : décident à quelles APIs l'access token donne
//         accès (catalog, basket, ordering, eshop).
//   - "ApiScope" vs "ApiResource" : un ApiScope est une simple permission nommée.
//     Un ApiResource regrouperait des scopes sous une AUDIENCE (claim "aud") qui
//     nommerait l'API destinataire. ICI on n'utilise QUE des ApiScopes, PAS
//     d'ApiResource => les access tokens ne portent AUCUN "aud" => les APIs
//     valident avec ValidateAudience = false (voir Catalog.API/Program.cs).
//
// LE FLUX (Authorization Code + PKCE), étape par étape :
//   1) Le front (client "webapp") redirige le navigateur vers /connect/authorize.
//   2) IdentityServer affiche la page de Login (Pages/Account/Login) ; l'utilisateur
//      s'authentifie (ASP.NET Core Identity vérifie le mot de passe).
//   3) IdentityServer renvoie au front un CODE d'autorisation court (RedirectUris).
//   4) Le front échange ce code contre les jetons sur /connect/token.
//   PKCE (Proof Key for Code Exchange) protège l'étape 3->4 contre l'interception
//   du code : le client prouve qu'il est bien celui qui a initié la demande.
//
// CE FICHIER décrit QUI peut demander QUOI, en trois listes : IdentityResources,
//   ApiScopes, Clients. Chargées "en mémoire" dans Program.cs via AddInMemory*
//   (idéal pour apprendre ; en prod on les stockerait en base / ConfigurationStore).
//
// PLACE DANS L'ENSEMBLE : premier fichier à lire pour Identity.API.
// ORDRE DE LECTURE CONSEILLÉ pour Identity.API :
//   1) Config.cs                       <-- vous êtes ici (le QUI/QUOI de l'OIDC)
//   2) Models/ApplicationUser.cs       (l'utilisateur)
//   3) Data/ApplicationDbContext.cs    (le stockage Identity)
//   4) Data/ApplicationDbContextSeed.cs(rôles + alice/bob)
//   5) Services/CustomProfileService.cs(injection des rôles dans les jetons)
//   6) Pages/Account/Login + Logout, Pages/Home/Error (les écrans UI)
//   7) Program.cs                      (le câblage Identity + IdentityServer)
// ============================================================================
public static class Config
{
    // IdentityResources : déterminent les claims d'identité présents dans l'ID Token,
    // selon les scopes demandés. Chaque scope OIDC mappe vers un ensemble de claims.
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),   // scope "openid" : obligatoire en OIDC, fournit le claim "sub" (identifiant utilisateur).
        new IdentityResources.Profile(),  // scope "profile" : claims de profil (name, given_name, ...).
        new IdentityResources.Email(),    // scope "email" : claims email / email_verified.
        // IdentityResource PERSONNALISÉE : on définit le scope "roles" qui, quand il
        // est demandé et accordé, fait apparaître le claim "role" dans le jeton.
        // Signature : (nom du scope, libellé, liste des claims associés au scope).
        // C'est le maillon qui permet ensuite à [Authorize(Roles = "Admin")] de
        // fonctionner côté APIs : sans ce scope, pas de claim "role" dans le jeton.
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
            // Client "PUBLIC" (SPA Blazor WASM) : son code tourne dans le navigateur,
            // donc tout secret qu'on y placerait serait lisible par n'importe qui.
            // Un client public ne peut donc PAS garder de secret confidentiel ;
            // on n'exige pas de client secret. (Un client "confidentiel", lui — ex.
            // un back-end serveur —, garderait un secret et s'authentifierait avec.)
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