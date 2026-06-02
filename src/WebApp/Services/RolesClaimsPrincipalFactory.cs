using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace WebApp.Services;

/// <summary>
/// Factory de principal personnalisée pour Blazor WebAssembly.
///
/// Identity.API émet les rôles dans le claim "role". Quand un utilisateur a plusieurs
/// rôles (ex. alice = Admin + Customer), ils arrivent sérialisés sous forme de tableau
/// JSON dans un unique claim ("[\"Admin\",\"Customer\"]"). Sans traitement, IsInRole
/// et &lt;AuthorizeView Roles="Admin"&gt; échouent.
///
/// Cette factory éclate ce tableau en claims "role" individuels afin que les rôles
/// soient correctement reconnus. À combiner avec options.UserOptions.RoleClaim = "role".
/// </summary>
public class RolesClaimsPrincipalFactory : AccountClaimsPrincipalFactory<RemoteUserAccount>
{
    public RolesClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
        : base(accessor)
    {
    }

    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account,
        RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (user.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
        {
            foreach (var roleClaim in identity.FindAll("role").ToArray())
            {
                var value = roleClaim.Value.Trim();
                if (value.StartsWith('['))
                {
                    identity.RemoveClaim(roleClaim);
                    var roles = JsonSerializer.Deserialize<string[]>(value) ?? [];
                    foreach (var role in roles)
                    {
                        identity.AddClaim(new Claim("role", role));
                    }
                }
            }
        }

        return user;
    }
}
