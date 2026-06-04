using Microsoft.AspNetCore.Identity;

namespace Identity.API.Models;

// ============================================================================
// FICHIER : ApplicationUser.cs  —  le MODÈLE d'utilisateur.
//
// CONCEPT : ASP.NET Core Identity est le système intégré de .NET pour gérer les
//   comptes (utilisateurs, mots de passe HACHÉS, rôles, connexions). Sa classe de
//   base IdentityUser fournit déjà Id, UserName, Email, le HASH du mot de passe
//   (jamais le mot de passe en clair), le verrouillage, etc.
//   Le PATTERN standard est d'HÉRITER d'IdentityUser pour ajouter ses propres
//   champs métier — ce qu'on fait ici avec Name / LastName.
//
// À LIRE après Config.cs, avant ApplicationDbContext.cs.
// ============================================================================
public class ApplicationUser : IdentityUser
{
    // Champs additionnels (prénom / nom), nullables car non obligatoires.
    public string? Name { get; set; }
    public string? LastName { get; set; }
}