using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// Émis par OrderProcessor (étape 2) lorsque la grace period d'une commande est écoulée
// sans annulation. Routing key : "ordering-grace-period-confirmed". Consommé par Ordering,
// qui passe alors la commande en AwaitingValidation puis valide le stock (simplifié).
// Id (hérité de IntegrationEvent) sert de clé d'idempotence côté consommateur.
public record GracePeriodConfirmedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
