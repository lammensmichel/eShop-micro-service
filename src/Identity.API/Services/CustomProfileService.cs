
using Duende.IdentityServer.AspNetIdentity;
using Duende.IdentityServer.Models;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Identity.API.Services;

// ProfileService personnalisé : IdentityServer l'appelle pour décider quels claims
// placer dans les jetons (ID Token / access token) émis pour un utilisateur.
// On l'enregistre dans Program.cs via .AddProfileService<CustomProfileService>().
// Ici, on enrichit le jeton avec les rôles de l'utilisateur, indispensables pour
// l'autorisation par rôle côté APIs ([Authorize(Roles = "Admin")]).
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