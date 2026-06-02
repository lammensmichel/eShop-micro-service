namespace WebApp.Models;

// DTO partagés du panier, utilisés par les pages Catalog.razor (ajout) et Basket.razor (affichage).
// Le BuyerId reste présent pour la sérialisation côté API mais il est toujours dérivé du
// jeton côté serveur (contrat anti-IDOR).
public class BasketDto
{
    public string BuyerId { get; set; } = string.Empty;
    public List<BasketItemDto> Items { get; set; } = [];
    public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
}

public class BasketItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice => UnitPrice * Quantity;
}
