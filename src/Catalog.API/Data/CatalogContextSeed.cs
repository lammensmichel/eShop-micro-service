using Catalog.API.Models;

namespace Catalog.API.Data;

public static class CatalogContextSeed
{
    public static async Task SeedAsync(CatalogDbContext context)
    {
        if (context.CatalogBrands.Any() || context.CatalogTypes.Any())
            return; // Déjà seedé, on ne refait pas

        var brands = new List<CatalogBrand>
        {
            new() { Name = "Azure" },
            new() { Name = ".NET" },
            new() { Name = "Visual Studio" },
            new() { Name = "SQL Server" },
            new() { Name = "Other" }
        };

        var types = new List<CatalogType>
        {
            new() { Type = "Mug" },
            new() { Type = "T-Shirt" },
            new() { Type = "Sheet" },
            new() { Type = "USB Memory Stick" }
        };

        await context.CatalogBrands.AddRangeAsync(brands);
        await context.CatalogTypes.AddRangeAsync(types);
        await context.SaveChangesAsync();

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

        await context.CatalogItems.AddRangeAsync(items);
        await context.SaveChangesAsync();
    }
}