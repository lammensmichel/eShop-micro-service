using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// Émis par PaymentProcessor (étape 2) lorsque le paiement d'une commande échoue.
// Routing key : "payment-failed". Consommé par Ordering, qui annule alors la commande.
// Id (hérité de IntegrationEvent) sert de clé d'idempotence côté consommateur.
public record OrderPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }

    // Motif d'échec du paiement (optionnel), à des fins de journalisation/diagnostic.
    public string? Reason { get; init; }
}
