namespace Basket.API.Models;

// Panier d'un acheteur. C'est l'unité stockée telle quelle dans Redis :
// l'objet entier est sérialisé en JSON sous une clé = BuyerId (cf. RedisBasketRepository).
// Basket.API ne possède pas de base relationnelle ; le panier est une donnée
// volatile/temporaire, idéale pour un cache clé-valeur comme Redis.
public class CustomerBasket
{
    // Identifiant de l'acheteur = clé Redis. Côté API il est TOUJOURS dérivé du
    // jeton JWT (claim "sub"), jamais d'une valeur fournie librement par le client
    // (protection anti-IDOR, cf. BasketApi.GetBuyerId).
    public required string BuyerId { get; set; }
    public List<BasketItem> Items { get; set; } = [];

    // Total calculé à la volée : aucune valeur stockée à maintenir cohérente,
    // on évite tout risque de désynchronisation entre les lignes et le total.
    public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
}