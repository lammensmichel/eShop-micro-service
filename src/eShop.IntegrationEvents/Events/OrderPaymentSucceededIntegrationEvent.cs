using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// Émis par PaymentProcessor (étape 2) lorsque le paiement d'une commande réussit.
// Routing key : "payment-succeeded". Consommé par Ordering, qui passe la commande en
// Paid puis Shipped.
// Id (hérité de IntegrationEvent) sert de clé d'idempotence côté consommateur.
public record OrderPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
