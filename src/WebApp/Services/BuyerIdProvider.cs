using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace WebApp.Services;

/// <summary>
/// Source unique de vérité pour l'identifiant de l'acheteur (BuyerId).
/// Toutes les pages doivent passer par ce service pour lire/écrire le panier
/// et les commandes, afin d'éviter les incohérences de clé (ex: "user1" en dur).
/// </summary>
public class BuyerIdProvider
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public BuyerIdProvider(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public async Task<string> GetBuyerIdAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous";
    }
}
