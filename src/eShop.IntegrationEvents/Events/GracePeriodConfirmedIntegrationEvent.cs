using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°3. Voir le schéma complet en tête de BasketCheckoutEvent.cs.
//
// QUI ÉMET     : OrderProcessor (étape 2), quand la "grace period" s'est écoulée
//                SANS que le client annule. La grace period est ce court délai de
//                grâce avant traitement réel : passé ce délai, la commande est ferme.
// ROUTING KEY  : "ordering-grace-period-confirmed".
// QUI CONSOMME : Ordering.API. Il fait alors avancer l'agrégat Order :
//                Submitted → AwaitingValidation, puis confirme le stock (simplifié ici).
// SUITE        : Ordering publie OrderStockConfirmedIntegrationEvent.
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record GracePeriodConfirmedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}
