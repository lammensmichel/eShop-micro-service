using Identity.API.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.API.Data;

// Seed des données de démo d'Identity : les deux rôles et les utilisateurs alice/bob.
// On passe par UserManager / RoleManager (et non par le DbContext brut) afin de
// bénéficier du hachage des mots de passe, de la normalisation des noms, etc.
public static class ApplicationDbContextSeed
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        // Crée les rôles
        // "Admin" : accès en écriture au catalogue (POST/PUT/DELETE).
        // "Customer" : client standard (panier, commandes).
        // Garde d'idempotence rôle par rôle : on ne (re)crée que s'il manque.
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        if (!await roleManager.RoleExistsAsync("Customer"))
            await roleManager.CreateAsync(new IdentityRole("Customer"));

        // Si des utilisateurs existent déjà, on ne re-seed pas (évite les doublons au redémarrage).
        if (userManager.Users.Any()) return;

        // Utilisateur de démo "alice" : sera Admin + Customer (peut tout faire).
        var alice = new ApplicationUser
        {
            UserName = "alice",
            Email = "alice@eshop.com",
            Name = "Alice",
            LastName = "Smith",
            EmailConfirmed = true   // email déjà confirmé : évite l'étape de validation en démo.
        };

        // Utilisateur de démo "bob" : simple Customer (pas d'accès admin au catalogue).
        var bob = new ApplicationUser
        {
            UserName = "bob",
            Email = "bob@eshop.com",
            Name = "Bob",
            LastName = "Jones",
            EmailConfirmed = true
        };

        // Création avec mot de passe : UserManager hache "Pass123$" avant de persister.
        // (Mot de passe identique pour les deux comptes, uniquement pour la démo.)
        await userManager.CreateAsync(alice, "Pass123$");
        await userManager.CreateAsync(bob, "Pass123$");

        // Attribution des rôles. alice cumule Admin + Customer ; bob n'est que Customer.
        // Ces rôles deviennent des claims "role" dans le jeton (cf. CustomProfileService).
        await userManager.AddToRoleAsync(alice, "Admin");
        await userManager.AddToRoleAsync(alice, "Customer");
        await userManager.AddToRoleAsync(bob, "Customer");
    }
}