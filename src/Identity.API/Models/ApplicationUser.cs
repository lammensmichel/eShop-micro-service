using Microsoft.AspNetCore.Identity;

namespace Identity.API.Models;

// Utilisateur de l'application. On hérite d'IdentityUser (ASP.NET Core Identity),
// qui fournit déjà Id, UserName, Email, hash du mot de passe, etc.
// On l'étend ici avec quelques champs métier propres à eShop.
public class ApplicationUser : IdentityUser
{
    // Champs additionnels (prénom / nom), nullables car non obligatoires.
    public string? Name { get; set; }
    public string? LastName { get; set; }
}