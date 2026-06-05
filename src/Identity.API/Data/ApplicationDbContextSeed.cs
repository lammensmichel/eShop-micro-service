using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Identity.API.Data;

// ============================================================================
// FICHIER : ApplicationDbContextSeed.cs  —  PEUPLEMENT initial d'Identity.
//
// RÔLE : créer au premier démarrage les deux rôles (Admin, Customer) et les deux
//   utilisateurs de démo (alice, bob), pour pouvoir se connecter immédiatement.
//
// CONCEPT — pourquoi UserManager / RoleManager et PAS le DbContext brut ?
//   UserManager<T> et RoleManager<T> sont les services « métier » d'ASP.NET Core
//   Identity. Insérer un utilisateur directement via le DbContext stockerait le
//   mot de passe tel quel et oublierait des règles importantes. En passant par
//   UserManager.CreateAsync(user, password), on bénéficie du HACHAGE du mot de
//   passe (transformation à sens unique : la base ne contient JAMAIS le mot de
//   passe en clair), de la normalisation des noms/emails, des validations, etc.
//
// IDEMPOTENCE : comme tout seed (cf. Catalog), ce code tourne à chaque démarrage ;
//   les gardes « si le rôle/l'utilisateur existe déjà, ne pas recréer » évitent
//   les doublons. (Voir CatalogContextSeed.cs pour la définition d'idempotence.)
//
// LIEN avec l'autorisation : les rôles attribués ici deviendront des claims "role"
//   dans le jeton, injectés par CustomProfileService.cs ; c'est ce qui décide qui
//   passe [Authorize(Roles = "Admin")] côté Catalog.API.
//
// À LIRE après ApplicationDbContext.cs, avant CustomProfileService.cs.
// ============================================================================
public static class ApplicationDbContextSeed
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration)
    {
        // ----- RÔLES : seed INCONDITIONNEL -----
        // Les rôles "Admin"/"Customer" sont nécessaires au fonctionnement de
        // l'autorisation ([Authorize(Roles=...)]) dans TOUS les environnements,
        // y compris en prod. On les seed donc toujours, indépendamment de la
        // présence ou non d'utilisateurs de démo.
        // "Admin" : accès en écriture au catalogue (POST/PUT/DELETE).
        // "Customer" : client standard (panier, commandes).
        // Garde d'idempotence rôle par rôle : on ne (re)crée que s'il manque.
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        if (!await roleManager.RoleExistsAsync("Customer"))
            await roleManager.CreateAsync(new IdentityRole("Customer"));

        // ----- UTILISATEURS DÉMO : seed CONDITIONNEL -----
        // POURQUOI conditionner : alice/bob avec un mot de passe connu sont pratiques
        // en dev mais constituent un TROU DE SÉCURITÉ en prod (comptes par défaut).
        //   - Dev local INCHANGÉ : appsettings.Development.json met "Identity:SeedDemoUsers"
        //     à true => alice/bob sont seedés exactement comme avant.
        //   - Prod : la clé vaut false (défaut hors Development) => AUCUN user démo créé.
        //
        // COMMENT CRÉER UN ADMIN EN PROD (hors seed) : ne PAS passer par ce seed.
        //   Provisionner un compte admin réel par un canal sûr, par exemple :
        //     - un job/CLI ponctuel appelant UserManager.CreateAsync + AddToRoleAsync,
        //       avec un mot de passe fort fourni par un secret (Kubernetes Secret),
        //     - ou la console d'administration une fois un premier admin amorcé.
        var seedDemoUsers = configuration.GetValue("Identity:SeedDemoUsers", false);
        if (!seedDemoUsers) return;

        // Si des utilisateurs existent déjà, on ne re-seed pas (évite les doublons au redémarrage).
        if (userManager.Users.Any()) return;

        // Mot de passe des comptes de démo, lu depuis la configuration.
        // Dev local INCHANGÉ : "Identity:DemoPassword" = "Pass123$" dans
        // appsettings.Development.json. Repli sur "Pass123$" par sécurité.
        var demoPassword = configuration["Identity:DemoPassword"] ?? "Pass123$";

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

        // Création avec mot de passe : UserManager hache le mot de passe (issu de la
        // config) avant de persister. (Mot de passe identique pour les deux comptes,
        // uniquement pour la démo.)
        await userManager.CreateAsync(alice, demoPassword);
        await userManager.CreateAsync(bob, demoPassword);

        // Attribution des rôles. alice cumule Admin + Customer ; bob n'est que Customer.
        // Ces rôles deviennent des claims "role" dans le jeton (cf. CustomProfileService).
        await userManager.AddToRoleAsync(alice, "Admin");
        await userManager.AddToRoleAsync(alice, "Customer");
        await userManager.AddToRoleAsync(bob, "Customer");
    }
}