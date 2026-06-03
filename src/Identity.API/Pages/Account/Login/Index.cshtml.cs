using Duende.IdentityServer.Services;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Account.Login;

// Code-behind de la page de connexion (Razor Page).
// C'est l'écran vers lequel le navigateur est redirigé pendant le flux Authorization Code :
// l'utilisateur saisit ses identifiants, puis on le renvoie vers IdentityServer (ReturnUrl).
public class LoginModel : PageModel
{
    // SignInManager : service Identity qui vérifie les identifiants et crée la session de connexion.
    private readonly SignInManager<ApplicationUser> _signInManager;
    // Service d'interaction IdentityServer : permet notamment de valider les ReturnUrl du flux OIDC.
    private readonly IIdentityServerInteractionService _interaction;

    // [BindProperty] : ces propriétés sont liées automatiquement aux champs du formulaire POST.
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    // ReturnUrl : où renvoyer l'utilisateur après connexion (l'endpoint d'autorisation OIDC).
    public string ReturnUrl { get; set; } = string.Empty;
    // Message d'erreur affiché en cas d'échec d'authentification.
    public string? ErrorMessage { get; set; }

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction)
    {
        _signInManager = signInManager;
        _interaction = interaction;
    }

    // GET : affichage initial du formulaire. On mémorise simplement le ReturnUrl reçu.
    public void OnGet(string returnUrl)
    {
        ReturnUrl = returnUrl ?? "/";
    }

    // POST : soumission du formulaire (tentative de connexion).
    public async Task<IActionResult> OnPost(string returnUrl)
    {
        ReturnUrl = returnUrl ?? "/";

        Console.WriteLine($"Login attempt: Username={Username}, ReturnUrl={ReturnUrl}");

        // Vérifie identifiants + mot de passe. isPersistent: false => cookie de session
        // (non persistant). lockoutOnFailure: false => pas de verrouillage après échecs (démo).
        var result = await _signInManager.PasswordSignInAsync(
            Username, Password, isPersistent: false, lockoutOnFailure: false);

        Console.WriteLine($"Login result: Succeeded={result.Succeeded}");

        if (result.Succeeded)
        {
            // Garde anti "open redirect" : on ne redirige que vers une ReturnUrl reconnue
            // par IdentityServer (flux OIDC) ou une URL locale ; sinon retour à l'accueil.
            if (_interaction.IsValidReturnUrl(ReturnUrl) || Url.IsLocalUrl(ReturnUrl))
                return Redirect(ReturnUrl);
            return Redirect("/");
        }

        // Échec : on réaffiche la page avec un message d'erreur générique
        // (on ne précise pas si c'est le login ou le mot de passe qui est faux).
        ErrorMessage = "Nom d'utilisateur ou mot de passe incorrect.";
        return Page();
    }
}