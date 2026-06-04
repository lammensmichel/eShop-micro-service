namespace Basket.API.Models;

// =============================================================================
// FICHIER : BasketItem.cs
// RÔLE    : une ligne du panier (un produit + sa quantité + son prix figé).
// CONCEPT : COPIE DÉNORMALISÉE vs source de vérité.
//
//   Le prix unitaire et le nom sont COPIÉS dans le panier au moment de l'ajout,
//   au lieu d'être relus en direct depuis Catalog.API. Pourquoi ?
//     - cohérence : le panier ne change pas de prix sous les yeux du client si le
//       catalogue est modifié entre-temps ;
//     - découplage : afficher le panier ne dépend pas de la disponibilité de
//       Catalog.API (pas d'appel synchrone inter-services).
//   En contrepartie, c'est une donnée de cache potentiellement "périmée" : la
//   source de vérité du prix reste Catalog.API.
//
// À LIRE : avec CustomerBasket.cs (le conteneur) et BasketCheckoutItem dans
//   eShop.IntegrationEvents/Events/BasketCheckoutEvent.cs (la copie envoyée à Ordering).
// =============================================================================
public class BasketItem
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    // Sous-total de la ligne, calculé à la volée (cf. CustomerBasket.TotalPrice).
    public decimal TotalPrice => UnitPrice * Quantity;
}