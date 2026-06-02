using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Identity.API.Models;

namespace Identity.API.Pages.Account.Logout;

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
        await _signInManager.SignOutAsync();

        var logout = await _interaction.GetLogoutContextAsync(logoutId);
        var postLogoutUri = logout?.PostLogoutRedirectUri;

        if (!string.IsNullOrEmpty(postLogoutUri))
            return Redirect(postLogoutUri);

        return Redirect("/");
    }
}