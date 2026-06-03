namespace Catalog.API.Models;

// Type/catégorie d'un produit (ex. "Mug", "T-Shirt"). Table de référence simple,
// reliée à CatalogItem par une relation 1-à-plusieurs (un type -> plusieurs items).
public class CatalogType
{
    public int Id { get; set; }
    // Type est indexé en unique dans le DbContext (pas de doublon de catégorie).
    public required string Type { get; set; }
}