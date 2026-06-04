
using Duende.IdentityServer.AspNetIdentity;
using Duende.IdentityServer.Models;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Identity.API.Services;

// ============================================================================
// FICHIER : CustomProfileService.cs  —  ce qui REMPLIT les jetons de claims.
//
// RÔLE : IdentityServer appelle un "ProfileService" au moment d'émettre un jeton,
//   pour décider quels CLAIMS y placer pour l'utilisateur concerné. On en fournit
//   une version personnalisée, enregistrée dans Program.cs via
//   .AddProfileService<CustomProfileService>().
//
// POURQUOI ce fichier existe — le maillon de l'AUTORISATION PAR RÔLE :
//   Les rôles d'un utilisateur (Admin, Customer) sont stockés côté Identity, mais
//   ils ne se retrouvent PAS automatiquement dans le jeton. Or les APIs décident
//   l'accès à partir des claims DU JETON ([Authorize(Roles = "Admin")] lit le
//   claim "role"). Il faut donc, ici, lire les rôles et les AJOUTER comme claims
//   "role" dans le jeton émis. Sans ce service, alice serait connectée mais son
//   jeton ne contiendrait pas "role=Admin", et l'écriture catalogue lui serait refusée.
//
// NOTE sur le NOM "role" (et MapInboundClaims côté API) : on émet délibérément le
//   claim sous le nom court "role". Côté APIs, .NET a tendance à RENOMMER les
//   claims standard entrants (ex. "role" -> une longue URI WS-*) ; pour que la
//   correspondance reste sur "role", la validation JWT laisse les noms tels quels
//   (MapInboundClaims = false) et déclare RoleClaimType = "role". Émetteur et
//   lecteur s'accordent ainsi sur le même nom de claim, sinon les rôles seraient
//   « invisibles » pour [Authorize(Roles=...)].
//
// À LIRE après ApplicationDbContextSeed.cs, avant les Pages/.
// ============================================================================
public class CustomProfileService : ProfileService<ApplicationUser>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomProfileService(
        UserManager<ApplicationUser> userManager,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory)
        : base(userManager, claimsFactory)
    {
        _userManager = userManager;
    }

    protected override async Task GetProfileDataAsync(
        ProfileDataRequestContext context,
        ApplicationUser user)
    {
        // On laisse d'abord le comportement de base ajouter les claims standard.
        await base.GetProfileDataAsync(context, user);

        // Puis on ajoute un claim "role" par rôle de l'utilisateur (Admin, Customer...).
        // IssuedClaims = liste des claims qui finiront dans le jeton émis.
        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            context.IssuedClaims.Add(new Claim("role", role));
        }

        // Claim "name" lisible (le nom d'utilisateur), pratique à afficher côté front.
        context.IssuedClaims.Add(new Claim("name", user.UserName ?? ""));
    }
}