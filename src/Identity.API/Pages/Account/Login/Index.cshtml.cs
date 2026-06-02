using Duende.IdentityServer.Services;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Account.Login;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;

    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction)
    {
        _signInManager = signInManager;
        _interaction = interaction;
    }

    public void OnGet(string returnUrl)
    {
        ReturnUrl = returnUrl ?? "/";
    }

    public async Task<IActionResult> OnPost(string returnUrl)
    {
        ReturnUrl = returnUrl ?? "/";

        Console.WriteLine($"Login attempt: Username={Username}, ReturnUrl={ReturnUrl}");

        var result = await _signInManager.PasswordSignInAsync(
            Username, Password, isPersistent: false, lockoutOnFailure: false);

        Console.WriteLine($"Login result: Succeeded={result.Succeeded}");

        if (result.Succeeded)
        {
            if (_interaction.IsValidReturnUrl(ReturnUrl) || Url.IsLocalUrl(ReturnUrl))
                return Redirect(ReturnUrl);
            return Redirect("/");
        }

        ErrorMessage = "Nom d'utilisateur ou mot de passe incorrect.";
        return Page();
    }
}