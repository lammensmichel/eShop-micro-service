namespace Catalog.API.Models;

// Produit du catalogue : c'est l'entité centrale de ce service.
// Remarque : contrairement à l'agrégat Order (Ordering.API), il s'agit ici d'un
// simple modèle de données anémique (propriétés publiques, pas d'invariants),
// car le catalogue est un domaine de type CRUD et non un cœur de métier riche.
public class CatalogItem
{
    // Clé primaire. EF Core la reconnaît par convention (propriété nommée "Id"),
    // et Postgres l'auto-incrémente (colonne identity).
    public int Id { get; set; }

    // "required" (C# 11) force ces propriétés à être initialisées à la création
    // de l'objet : c'est une garantie de compilation, pas une contrainte EF.
    public required string Name { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public required string PictureFileName { get; set; }
    public int AvailableStock { get; set; }

    // Clé étrangère + propriété de navigation vers le type (Mug, T-Shirt, ...).
    // Par convention EF, "CatalogTypeId" est reconnue comme la FK de "CatalogType".
    public int CatalogTypeId { get; set; }
    // "= null!" rassure le compilateur sur le nullable : EF remplira la navigation
    // au chargement (via Include), donc on promet qu'elle ne sera pas null à l'usage.
    public CatalogType CatalogType { get; set; } = null!;

    // Même schéma de relation pour la marque (Azure, .NET, ...).
    public int CatalogBrandId { get; set; }
    public CatalogBrand CatalogBrand { get; set; } = null!;
}