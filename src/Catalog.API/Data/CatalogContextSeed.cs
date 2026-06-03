using Catalog.API.Models;

namespace Catalog.API.Data;

// Seed = jeu de données initial inséré au démarrage (appelé depuis Program.cs,
// juste après l'application des migrations). Permet d'avoir un catalogue
// fonctionnel sans saisie manuelle dès le premier lancement.
public static class CatalogContextSeed
{
    public static async Task SeedAsync(CatalogDbContext context)
    {
        // Garde d'idempotence : si la base contient déjà des données de référence,
        // on s'arrête. Ainsi un redémarrage de l'API ne réinsère pas de doublons.
        if (context.CatalogBrands.Any() || context.CatalogTypes.Any())
            return; // Déjà seedé, on ne refait pas

        // Tables de référence : les marques disponibles.
        var brands = new List<CatalogBrand>
        {
            new() { Name = "Azure" },
            new() { Name = ".NET" },
            new() { Name = "Visual Studio" },
            new() { Name = "SQL Server" },
            new() { Name = "Other" }
        };

        // Tables de référence : les types/catégories de produits.
        var types = new List<CatalogType>
        {
            new() { Type = "Mug" },
            new() { Type = "T-Shirt" },
            new() { Type = "Sheet" },
            new() { Type = "USB Memory Stick" }
        };

        // On persiste d'abord marques et types : EF leur attribue alors leurs Id,
        // qui serviront ensuite à rattacher les produits via les navigations.
        await context.CatalogBrands.AddRangeAsync(brands);
        await context.CatalogTypes.AddRangeAsync(types);
        await context.SaveChangesAsync();

        // Produits du catalogue. On référence les marques/types par instance
        // (CatalogBrand = brands[1], ...) plutôt que par Id : EF déduit la clé
        // étrangère depuis la navigation, ce qui est plus lisible dans un seed.
        var items = new List<CatalogItem>
        {
            new()
            {
                Name = ".NET Bot Black Sweatshirt",
                Description = "Classic .NET Bot sweatshirt in black",
                Price = 19.5m,
                PictureFileName = "1.png",
                CatalogBrand = brands[1],
                CatalogType = types[1],
                AvailableStock = 100
            },
            new()
            {
                Name = ".NET Black & White Mug",
                Description = "Black and white mug with .NET logo",
                Price = 8.50m,
                PictureFileName = "2.png",
                CatalogBrand = brands[1],
                CatalogType = types[0],
                AvailableStock = 50
            },
            new()
            {
                Name = "Prism White T-Shirt",
                Description = "White t-shirt with prism logo",
                Price = 12.0m,
                PictureFileName = "3.png",
                CatalogBrand = brands[4],
                CatalogType = types[1],
                AvailableStock = 75
            },
            new()
            {
                Name = "Azure Mug",
                Description = "Azure logo mug",
                Price = 9.0m,
                PictureFileName = "4.png",
                CatalogBrand = brands[0],
                CatalogType = types[0],
                AvailableStock = 200
            }
        };

        // Second SaveChanges : insère les produits une fois leurs dépendances en base.
        await context.CatalogItems.AddRangeAsync(items);
        await context.SaveChangesAsync();
    }
}