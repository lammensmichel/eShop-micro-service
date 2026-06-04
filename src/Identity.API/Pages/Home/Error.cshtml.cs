using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Home;

// ============================================================================
// FICHIER : Home/Error.cshtml.cs  —  code-behind de la page d'ERREUR OIDC.
//
// RÔLE : quand un flux OIDC échoue (client inconnu, RedirectUri non déclaré,
//   scope refusé...), IdentityServer ne renvoie pas un message brut : il redirige
//   le navigateur ici avec un "errorId". Ce code récupère, via le service
//   d'interaction d'IdentityServer, le détail d'erreur associé à cet identifiant
//   pour que le markup (.cshtml) puisse l'afficher proprement.
//
// À LIRE après les pages Login/Logout ; c'est le filet de sécurité du flux.
// ============================================================================
public class ErrorModel : PageModel
{
    private readonly IIdentityServerInteractionService _interaction;

    // Détail de l'erreur récupéré côté serveur (affiché ensuite par le markup .cshtml).
    public ErrorMessage? Error { get; set; }

    public ErrorModel(IIdentityServerInteractionService interaction)
    {
        _interaction = interaction;
    }

    public async Task OnGet(string errorId)
    {
        // Récupère le contexte d'erreur associé à l'identifiant fourni par IdentityServer.
        Error = await _interaction.GetErrorContextAsync(errorId);
    }
}

