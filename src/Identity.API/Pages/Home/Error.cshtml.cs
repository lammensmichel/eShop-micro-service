using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Home;

// Code-behind de la page d'erreur d'IdentityServer.
// En cas de problème dans un flux OIDC (client inconnu, scope refusé, etc.),
// IdentityServer redirige ici avec un "errorId" pointant vers le détail de l'erreur.
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

