using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Apis;

// Regroupe les endpoints du catalogue (style "minimal API").
// La méthode d'extension MapCatalogApi est appelée dans Program.cs (app.MapCatalogApi()).
// Sécurité : la lecture (GET) est PUBLIQUE, l'écriture (POST/PUT/DELETE) est réservée
// au rôle "Admin" via l'attribut [Authorize(Roles = "Admin")] sur chaque endpoint.
public static class CatalogApi
{
    public static RouteGroupBuilder MapCatalogApi(this IEndpointRouteBuilder app)
    {
        // Préfixe commun à toutes les routes ci-dessous : /api/catalog/...
        var group = app.MapGroup("/api/catalog");

        // GET — accessible à tous (même non connecté)
        group.MapGet("/items", async (CatalogDbContext db) =>
        {
            // Le DbContext est injecté automatiquement par le conteneur DI dans la lambda.
            // Include charge les navigations marque/type (jointures) pour les renvoyer
            // dans la réponse ; sinon elles seraient null (pas de lazy loading ici).
            var items = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .ToListAsync();
            return Results.Ok(items);
        });

        // GET d'un produit par son Id ("{id:int}" contraint le paramètre de route à un entier).
        group.MapGet("/items/{id:int}", async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .FirstOrDefaultAsync(i => i.Id == id);
            // 404 si introuvable, sinon 200 + le produit.
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        // POST/PUT/DELETE — Admin seulement
        // L'attribut [Authorize(Roles = "Admin")] exige un jeton valide portant le rôle
        // "Admin" (claim "role"). Seule "alice" possède ce rôle parmi les utilisateurs seedés.
        group.MapPost("/items", [Authorize(Roles = "Admin")] async (CatalogItem item, CatalogDbContext db) =>
        {
            db.CatalogItems.Add(item);
            await db.SaveChangesAsync();
            // 201 Created + en-tête Location pointant vers la ressource nouvellement créée.
            return Results.Created($"/api/catalog/items/{item.Id}", item);
        });

        // PUT — mise à jour complète d'un produit existant (Admin seulement).
        group.MapPut("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogItem item, CatalogDbContext db) =>
        {
            // On recharge l'entité suivie par EF puis on recopie les champs : ainsi
            // le change tracker génère un UPDATE ciblé au SaveChanges.
            var existing = await db.CatalogItems.FindAsync(id);
            if (existing is null) return Results.NotFound();

            existing.Name = item.Name;
            existing.Description = item.Description;
            existing.Price = item.Price;
            existing.AvailableStock = item.AvailableStock;
            existing.CatalogBrandId = item.CatalogBrandId;
            existing.CatalogTypeId = item.CatalogTypeId;

            await db.SaveChangesAsync();
            return Results.Ok(existing);
        });

        // DELETE — suppression d'un produit (Admin seulement).
        group.MapDelete("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            db.CatalogItems.Remove(item);
            await db.SaveChangesAsync();
            // 204 No Content : succès sans corps de réponse.
            return Results.NoContent();
        });

        // Endpoints pour brands et types (Admin)
        // Note : ces GET de référence sont en réalité publics (pas de [Authorize]),
        // comme les GET de produits ; ils servent à alimenter les listes déroulantes du front.
        group.MapGet("/brands", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogBrands.ToListAsync()));

        group.MapGet("/types", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogTypes.ToListAsync()));

        // On renvoie le groupe pour permettre un éventuel chaînage côté appelant.
        return group;
    }
}