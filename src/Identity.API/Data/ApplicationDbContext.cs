using Identity.API.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.API.Data;

// DbContext d'Identity. Il hérite d'IdentityDbContext<ApplicationUser>, qui définit
// déjà tout le schéma d'ASP.NET Core Identity : tables des utilisateurs (AspNetUsers),
// rôles (AspNetRoles), associations user/rôle, claims, logins externes, jetons, etc.
// On n'a donc aucun DbSet ni mapping à écrire ici : tout vient de la classe de base.
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }
}