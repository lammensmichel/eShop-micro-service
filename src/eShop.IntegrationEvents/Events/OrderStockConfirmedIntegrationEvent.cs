using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°4. Voir le schéma complet en tête de BasketCheckoutEvent.cs.
//
// QUI ÉMET     : Ordering.API (via l'outbox, cf. note outbox dans
//                OrderStatusChangedToSubmittedIntegrationEvent.cs) une fois le stock
//                confirmé pour la commande.
// ROUTING KEY  : "ordering-order-stock-confirmed".
// QUI CONSOMME : PaymentProcessor (étape 2), qui SIMULE le paiement et répond par
//                l'un des deux événements terminaux ci-dessous.
// SUITE (branche) : OrderPaymentSucceededIntegrationEvent (succès)
//                   OU OrderPaymentFailedIntegrationEvent (échec).
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record OrderStockConfirmedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
