using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// Émis (via l'outbox d'Ordering.API) lorsque le stock d'une commande est confirmé.
// Routing key : "ordering-order-stock-confirmed". Consommé par PaymentProcessor (étape 2),
// qui simule alors le paiement.
// Id (hérité de IntegrationEvent) sert de clé d'idempotence côté consommateur.
public record OrderStockConfirmedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
