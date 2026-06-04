namespace Catalog.API.Models;

// ============================================================================
// FICHIER : CatalogBrand.cs  —  table de RÉFÉRENCE (lookup) du catalogue.
//
// RÔLE : la marque d'un produit (ex. "Azure", ".NET"). Une « table de référence »
//        est une petite table de valeurs réutilisables auxquelles les lignes
//        principales se rattachent — ici, plusieurs CatalogItem pointent vers
//        une même CatalogBrand (relation 1-à-plusieurs : 1 marque -> N produits).
//
// CONCEPT : c'est l'autre bout de la relation décrite dans CatalogItem.cs.
//        Dans CatalogItem on avait la FK (CatalogBrandId) + la navigation
//        (CatalogBrand) ; ici, on a juste l'entité « parente ».
//
// À LIRE après CatalogItem.cs, en parallèle de CatalogType.cs (même structure).
// ============================================================================
public class CatalogBrand
{
    public int Id { get; set; }
    // "required" (C# 11) : force à fournir Name à la construction de l'objet.
    // C'est une garantie de COMPILATION (et non une contrainte EF/base).
    // Par ailleurs, Name est indexé en UNIQUE dans le DbContext (OnModelCreating) :
    // deux marques ne peuvent pas porter le même nom.
    public required string Name { get; set; }
}