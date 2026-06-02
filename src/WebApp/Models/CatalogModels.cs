namespace WebApp.Models;

// DTO partagés du catalogue, utilisés par les pages Catalog.razor et Admin.razor.
// On expose toutes les propriétés renvoyées par Catalog.API ; chaque page n'utilise
// que ce dont elle a besoin (le stock n'est affiché que côté Admin).
public record CatalogItemDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int AvailableStock,
    BrandDto? CatalogBrand,
    TypeDto? CatalogType);

public record BrandDto(int Id, string Name);

public record TypeDto(int Id, string Type);
