
using Duende.IdentityServer.AspNetIdentity;
using Duende.IdentityServer.Models;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Identity.API.Services;

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
        await base.GetProfileDataAsync(context, user);

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            context.IssuedClaims.Add(new Claim("role", role));
        }

        context.IssuedClaims.Add(new Claim("name", user.UserName ?? ""));
    }
}