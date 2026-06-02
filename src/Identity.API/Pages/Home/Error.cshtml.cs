using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Home;

public class ErrorModel : PageModel
{
    private readonly IIdentityServerInteractionService _interaction;

    public ErrorMessage? Error { get; set; }

    public ErrorModel(IIdentityServerInteractionService interaction)
    {
        _interaction = interaction;
    }

    public async Task OnGet(string errorId)
    {
        Error = await _interaction.GetErrorContextAsync(errorId);
    }
}

