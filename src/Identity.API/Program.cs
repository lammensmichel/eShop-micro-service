using Identity.API;
using Identity.API.Data;
using Identity.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Identity.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ApplicationDbContext>("identitydb");

// Politique CORS restreinte à l'origine du front (lue depuis la configuration "Cors:AllowedOrigins").
// Repli dev raisonnable si la configuration est absente.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:7204", "http://localhost:5274" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddIdentityServer(options =>
    {
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
    })
    .AddInMemoryIdentityResources(Config.IdentityResources)
    .AddInMemoryApiScopes(Config.ApiScopes)
    .AddInMemoryClients(Config.Clients)
    .AddAspNetIdentity<ApplicationUser>()
    .AddProfileService<CustomProfileService>();

builder.Services.AddRazorPages();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
// UseCors doit rester avant UseIdentityServer.
app.UseCors("FrontendCors");
app.UseIdentityServer();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!await db.Database.CanConnectAsync())
    throw new Exception("Cannot connect to database");
    Console.WriteLine("🔄 Applying migrations...");
    await db.Database.MigrateAsync();
    Console.WriteLine("✅ Migrations applied!");
    
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    Console.WriteLine("🌱 Starting seed...");
    await ApplicationDbContextSeed.SeedAsync(db, userManager, roleManager);
    Console.WriteLine("✅ Seed complete!");
}

app.MapGet("/", () => "Identity.API is running!");

app.Run();