namespace Basket.API.Models;

// =============================================================================
// FICHIER : BasketCheckout.cs
// RÔLE    : le DTO (objet de transfert) reçu dans le corps de POST /checkout.
// CONCEPT : ENTRÉE UTILISATEUR vs ÉTAT SERVEUR.
//
//   Ce modèle ne porte QUE ce que le client doit fournir au moment de valider :
//   adresse de livraison + infos de carte. Les LIGNES et le TOTAL, eux, ne sont
//   PAS pris dans la requête : ils sont lus côté serveur depuis le panier Redis
//   (on ne fait jamais confiance au client pour le contenu/montant facturé).
//   BasketApi fusionne ces deux sources pour bâtir le BasketCheckoutEvent.
//
// À LIRE : juste avant BasketApi.cs (handler /checkout) et
//   eShop.IntegrationEvents/Events/BasketCheckoutEvent.cs (l'événement résultant).
// =============================================================================
public class BasketCheckout
{
    // Présent dans le contrat mais IGNORÉ côté serveur : le BuyerId réel est
    // dérivé du jeton (anti-IDOR, cf. BasketApi). Voir aussi CustomerBasket.BuyerId.
    public required string BuyerId { get; set; }
    public required string City { get; set; }
    public required string Street { get; set; }
    public required string Country { get; set; }
    public required string ZipCode { get; set; }
    public required string CardNumber { get; set; }
    public required string CardHolderName { get; set; }
    public DateTime CardExpiration { get; set; }
}