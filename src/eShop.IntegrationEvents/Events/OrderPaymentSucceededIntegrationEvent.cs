using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°5a — BRANCHE SUCCÈS (terminale). Voir le schéma complet en
// tête de BasketCheckoutEvent.cs.
//
// QUI ÉMET     : PaymentProcessor (étape 2) lorsque le paiement réussit.
// ROUTING KEY  : "payment-succeeded".
// QUI CONSOMME : Ordering.API, qui fait avancer l'agrégat : Paid → Shipped.
// C'est la fin "heureuse" de la saga. Le pendant "échec" est
// OrderPaymentFailedIntegrationEvent (annule la commande). Les deux branches sont
// mutuellement exclusives : PaymentProcessor n'émet que l'une OU l'autre.
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record OrderPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
