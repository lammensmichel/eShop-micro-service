using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Apis;

public static class CatalogApi
{
    public static RouteGroupBuilder MapCatalogApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog");

        // GET — accessible à tous (même non connecté)
        group.MapGet("/items", async (CatalogDbContext db) =>
        {
            var items = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .ToListAsync();
            return Results.Ok(items);
        });

        group.MapGet("/items/{id:int}", async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .FirstOrDefaultAsync(i => i.Id == id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        // POST/PUT/DELETE — Admin seulement
        group.MapPost("/items", [Authorize(Roles = "Admin")] async (CatalogItem item, CatalogDbContext db) =>
        {
            db.CatalogItems.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/catalog/items/{item.Id}", item);
        });

        group.MapPut("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogItem item, CatalogDbContext db) =>
        {
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

        group.MapDelete("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            db.CatalogItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Endpoints pour brands et types (Admin)
        group.MapGet("/brands", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogBrands.ToListAsync()));

        group.MapGet("/types", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogTypes.ToListAsync()));

        return group;
    }
}