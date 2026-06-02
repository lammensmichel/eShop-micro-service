namespace Catalog.API.Models;

public class CatalogItem
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public required string PictureFileName { get; set; }
    public int AvailableStock { get; set; }

    public int CatalogTypeId { get; set; }
    public CatalogType CatalogType { get; set; } = null!;

    public int CatalogBrandId { get; set; }
    public CatalogBrand CatalogBrand { get; set; } = null!;
}