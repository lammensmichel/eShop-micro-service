using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// Émis (via l'outbox d'Ordering.API) lorsqu'une commande vient d'être créée et passe
// au statut « Submitted ». C'est le point d'entrée de la chorégraphie de saga (Chantier B).
// Id (hérité de IntegrationEvent) sert de clé d'idempotence côté consommateur.
public record OrderStatusChangedToSubmittedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required string OrderStatus { get; init; }
}
