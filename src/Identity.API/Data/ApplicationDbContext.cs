using Identity.API.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.API.Data;

// ============================================================================
// FICHIER : ApplicationDbContext.cs  —  la PORTE D'ENTRÉE vers la base Identity.
//
// RAPPEL (cf. Catalog.API/Data/CatalogDbContext.cs pour la définition complète) :
//   un DbContext est la session EF Core qui parle à la base (ici "identitydb").
//
// DIFFÉRENCE-CLÉ : ici on n'hérite PAS du DbContext nu, mais d'
//   IdentityDbContext<ApplicationUser>. Cette classe de base définit DÉJÀ tout le
//   schéma d'ASP.NET Core Identity : tables des utilisateurs (AspNetUsers), des
//   rôles (AspNetRoles), des associations user<->rôle, des claims, des logins
//   externes, des jetons... C'est pourquoi ce fichier est quasi vide : aucun DbSet
//   ni mapping à écrire, tout vient de la classe de base (les migrations Identity
//   créent ces tables pour nous).
//
// À LIRE après ApplicationUser.cs, avant ApplicationDbContextSeed.cs.
// ============================================================================
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }
}