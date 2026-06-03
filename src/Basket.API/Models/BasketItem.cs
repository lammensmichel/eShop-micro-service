namespace Basket.API.Models;

// Une ligne du panier. Le prix unitaire est figé dans le panier (copie au moment
// de l'ajout) plutôt que relu depuis Catalog.API : le panier reste cohérent même
// si le prix catalogue évolue. C'est une donnée de cache, pas la source de vérité.
public class BasketItem
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    // Sous-total de la ligne, calculé à la volée (cf. CustomerBasket.TotalPrice).
    public decimal TotalPrice => UnitPrice * Quantity;
}