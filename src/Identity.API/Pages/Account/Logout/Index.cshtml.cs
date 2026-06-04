using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Identity.API.Models;

namespace Identity.API.Pages.Account.Logout;

// ============================================================================
// FICHIER : Logout/Index.cshtml.cs  —  code-behind de la page de DÉCONNEXION.
//
// PLACE DANS LE FLUX OIDC : pendant du Login. Le front déclenche un logout OIDC ;
//   IdentityServer route le navigateur ici. Le paramètre "logoutId" identifie le
//   contexte de déconnexion côté serveur (il porte notamment l'URL de retour
//   post-logout déclarée par le client dans Config.cs / PostLogoutRedirectUris).
//
// Ce qu'on fait : supprimer le cookie de session (SignOutAsync) puis renvoyer le
//   navigateur vers cette URL de retour validée, sinon vers l'accueil.
//
// À LIRE juste après Login/Index.cshtml.cs.
// ============================================================================
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;

    public LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction)
    {
        _signInManager = signInManager;
        _interaction = interaction;
    }

    public async Task<IActionResult> OnGet(string logoutId)
    {
        // Supprime le cookie de session : l'utilisateur est déconnecté d'IdentityServer.
        await _signInManager.SignOutAsync();

        // Récupère le contexte de déconnexion pour connaître l'URL de retour du client
        // (PostLogoutRedirectUris déclarée dans Config.cs).
        var logout = await _interaction.GetLogoutContextAsync(logoutId);
        var postLogoutUri = logout?.PostLogoutRedirectUri;

        // Si une URL de retour valide existe, on y renvoie le navigateur ; sinon accueil.
        if (!string.IsNullOrEmpty(postLogoutUri))
            return Redirect(postLogoutUri);

        return Redirect("/");
    }
}