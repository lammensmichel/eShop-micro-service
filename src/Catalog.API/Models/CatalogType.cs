namespace Catalog.API.Models;

// ============================================================================
// FICHIER : CatalogType.cs  —  table de RÉFÉRENCE (lookup) du catalogue.
//
// RÔLE : le type/catégorie d'un produit (ex. "Mug", "T-Shirt"). Même rôle et
//        même structure que CatalogBrand : une table de référence reliée à
//        CatalogItem par une relation 1-à-plusieurs (1 type -> N produits).
//
// À LIRE en parallèle de CatalogBrand.cs (jumeau structurel).
// ============================================================================
public class CatalogType
{
    public int Id { get; set; }
    // "required" : Type doit être fourni à la création (garantie de compilation).
    // Indexé en UNIQUE dans le DbContext : pas de doublon de catégorie.
    public required string Type { get; set; }
}