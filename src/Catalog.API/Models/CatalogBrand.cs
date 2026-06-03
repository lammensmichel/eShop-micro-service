namespace Catalog.API.Models;

// Marque d'un produit (ex. "Azure", ".NET"). Table de référence simple,
// reliée à CatalogItem par une relation 1-à-plusieurs (une marque -> plusieurs items).
public class CatalogBrand
{
    public int Id { get; set; }
    // Name est indexé en unique dans le DbContext (deux marques ne peuvent
    // pas porter le même nom).
    public required string Name { get; set; }
}