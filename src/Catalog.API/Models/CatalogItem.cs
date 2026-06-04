namespace Catalog.API.Models;

// ============================================================================
// FICHIER : CatalogItem.cs  —  le MODÈLE de données central de Catalog.API.
//
// RÔLE : représenter un produit du catalogue (un "item"). EF Core va mapper
//        cette classe sur une table Postgres ; chaque instance = une ligne.
//
// CONCEPT ILLUSTRÉ : le « modèle anémique ».
//   - Modèle ANÉMIQUE = une classe qui n'est qu'un sac de propriétés publiques,
//     sans comportement ni règle métier (pas de méthodes qui protègent l'état).
//     On peut tout lire et tout modifier de l'extérieur via les setters publics.
//   - À l'opposé, un « agrégat riche » (DDD) encapsule ses règles (invariants)
//     dans des méthodes et garde ses setters privés. CONTRASTE concret : va voir
//     l'agrégat Order dans Ordering.API (Domain/AggregatesModel/OrderAggregate/Order.cs) :
//     ses propriétés ont des setters privés, et on ne peut le faire évoluer que
//     par des méthodes (Ship(), Cancel()...) qui vérifient la cohérence.
//   - POURQUOI anémique ICI ? Le catalogue est un domaine « CRUD » (Create-Read-
//     Update-Delete) sans logique métier complexe : un modèle riche serait du
//     sur-investissement. On réserve l'agrégat riche au cœur de métier (la commande).
//
// PLACE DANS L'ENSEMBLE : c'est le point de départ de Catalog.API. Les autres
//   pièces tournent autour de lui (le DbContext le persiste, le seed le remplit,
//   l'API l'expose).
//
// ORDRE DE LECTURE CONSEILLÉ pour découvrir Catalog.API :
//   1) Models/CatalogItem.cs        <-- vous êtes ici (le quoi)
//   2) Models/CatalogBrand.cs + Models/CatalogType.cs (les tables de référence)
//   3) Data/CatalogDbContext.cs     (comment on parle à la base : DbContext, mapping)
//   4) Data/CatalogContextSeed.cs   (le jeu de données initial)
//   5) Apis/CatalogApi.cs           (les endpoints HTTP)
//   6) Program.cs                   (le câblage de tout au démarrage)
// ============================================================================
public class CatalogItem
{
    // Clé primaire. EF Core la reconnaît PAR CONVENTION : une propriété nommée
    // "Id" (ou "<NomClasse>Id") devient automatiquement la clé primaire, sans
    // configuration. Côté base, Postgres l'auto-incrémente (colonne "identity").
    public int Id { get; set; }

    // "required" (C# 11) force ces propriétés à être initialisées à la création
    // de l'objet : c'est une garantie de compilation, pas une contrainte EF.
    public required string Name { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public required string PictureFileName { get; set; }
    public int AvailableStock { get; set; }

    // RELATION 1-à-plusieurs. On modélise « un produit appartient à un type »
    // avec DEUX membres complémentaires :
    //   - CatalogTypeId  : la CLÉ ÉTRANGÈRE (FK). Une FK est la colonne qui stocke
    //     l'Id de la ligne liée dans l'autre table ; c'est le lien réel en base.
    //   - CatalogType    : la PROPRIÉTÉ DE NAVIGATION. Côté C#, elle donne accès à
    //     l'objet lié directement (item.CatalogType.Type) sans manipuler l'Id.
    // Par convention EF, "CatalogTypeId" est automatiquement reconnue comme la FK
    // associée à la navigation "CatalogType" (nom de navigation + "Id").
    public int CatalogTypeId { get; set; }
    // "= null!" : on dit au compilateur « je promets que ce ne sera pas null à
    // l'usage ». EF remplit cette navigation au chargement quand on la demande
    // explicitement (via .Include(...), voir CatalogApi.cs) ; sans Include elle
    // resterait null (pas de lazy loading configuré ici).
    public CatalogType CatalogType { get; set; } = null!;

    // Même schéma de relation pour la marque (Azure, .NET, ...).
    public int CatalogBrandId { get; set; }
    public CatalogBrand CatalogBrand { get; set; } = null!;
}