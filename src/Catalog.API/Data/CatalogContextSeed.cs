using Catalog.API.Models;

namespace Catalog.API.Data;

// ============================================================================
// FICHIER : CatalogContextSeed.cs  —  le PEUPLEMENT initial de la base.
//
// CONCEPT : « seed » (semer). C'est l'insertion d'un jeu de données de départ
//   au tout premier démarrage, pour que l'application soit utilisable sans
//   saisie manuelle. Appelé depuis Program.cs JUSTE APRÈS l'application des
//   migrations (la base existe, mais elle est vide).
//
// CONCEPT-CLÉ : l'IDEMPOTENCE. Une opération est « idempotente » si l'exécuter
//   plusieurs fois produit le même état final qu'une seule fois. Le service
//   redémarre souvent (dev, conteneurs Aspire) et ce code tourne à CHAQUE
//   démarrage ; sans garde, on réinsérerait les mêmes produits en double. La
//   garde ci-dessous (« si déjà rempli, on sort ») rend le seed idempotent.
//
// À LIRE après CatalogDbContext.cs, avant Apis/CatalogApi.cs.
// ============================================================================
public static class CatalogContextSeed
{
    public static async Task SeedAsync(CatalogDbContext context)
    {
        // GARDE D'IDEMPOTENCE : si la base contient déjà des données de référence,
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

        // ORDRE D'INSERTION EN DEUX TEMPS — pourquoi ? Les produits référencent une
        // marque et un type. On persiste donc d'abord les tables de référence : au
        // SaveChanges, EF demande à Postgres les Id auto-générés et remet à jour les
        // objets "brands"/"types" en mémoire. Ces objets, désormais porteurs de leur
        // Id, pourront servir à rattacher les produits via leurs navigations.
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