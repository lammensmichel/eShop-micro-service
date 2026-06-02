using Identity.API.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.API.Data;

public static class ApplicationDbContextSeed
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        // Crée les rôles
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        if (!await roleManager.RoleExistsAsync("Customer"))
            await roleManager.CreateAsync(new IdentityRole("Customer"));

        if (userManager.Users.Any()) return;

        var alice = new ApplicationUser
        {
            UserName = "alice",
            Email = "alice@eshop.com",
            Name = "Alice",
            LastName = "Smith",
            EmailConfirmed = true
        };

        var bob = new ApplicationUser
        {
            UserName = "bob",
            Email = "bob@eshop.com",
            Name = "Bob",
            LastName = "Jones",
            EmailConfirmed = true
        };

        await userManager.CreateAsync(alice, "Pass123$");
        await userManager.CreateAsync(bob, "Pass123$");

        await userManager.AddToRoleAsync(alice, "Admin");
        await userManager.AddToRoleAsync(alice, "Customer");
        await userManager.AddToRoleAsync(bob, "Customer");
    }
}