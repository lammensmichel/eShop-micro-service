using Duende.IdentityServer.Services;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Identity.API.Pages.Account.Login;

// ============================================================================
// FICHIER : Login/Index.cshtml.cs  —  code-behind de la page de CONNEXION.
//
// CONCEPT : une Razor Page sépare le MARKUP (Index.cshtml, hors de notre champ)
//   du « code-behind » (ce fichier). PageModel = la classe qui porte l'état de la
//   page et ses gestionnaires OnGet/OnPost.
//
// PLACE DANS LE FLUX OIDC (Authorization Code + PKCE, cf. Config.cs) :
//   c'est l'écran vers lequel IdentityServer redirige le navigateur à l'étape 2
//   (« l'utilisateur s'authentifie »). L'utilisateur saisit identifiant + mot de
//   passe ; en cas de succès on le renvoie vers le ReturnUrl, c.-à-d. l'endpoint
//   d'autorisation OIDC qui poursuivra l'émission du code puis des jetons.
//
// À LIRE avec Logout/Index.cshtml.cs (le pendant déconnexion).
// ============================================================================
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

        // PasswordSignInAsync vérifie l'identifiant et compare le mot de passe à son
        // HASH stocké (jamais comparé en clair), puis crée le cookie de session.
        // isPersistent: false => cookie de session (disparaît à la fermeture du
        // navigateur). lockoutOnFailure: false => pas de verrouillage après plusieurs
        // échecs (acceptable en démo ; en prod on le mettrait à true).
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