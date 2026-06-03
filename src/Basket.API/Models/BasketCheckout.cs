namespace Basket.API.Models;

// Données saisies par le client au moment de valider la commande (adresse de
// livraison + informations de carte). C'est le corps de la requête POST /checkout.
// Ces champs viennent COMPLÉTER le contenu du panier (lu côté serveur) pour
// construire le BasketCheckoutEvent publié sur le bus vers Ordering.API.
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