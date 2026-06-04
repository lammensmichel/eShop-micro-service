using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°2 (après BasketCheckoutEvent). Voir le schéma complet en tête
// de BasketCheckoutEvent.cs.
//
// QUI ÉMET    : Ordering.API, juste après avoir créé l'agrégat Order (statut Submitted).
// COMMENT     : via le pattern OUTBOX. Plutôt que de publier directement sur le bus
//               (ce qui pourrait réussir alors que la transaction BD échoue, ou
//               l'inverse), l'événement est d'abord ENREGISTRÉ dans une table outbox
//               DANS LA MÊME TRANSACTION que la commande. Un processus publie ensuite
//               la ligne outbox sur RabbitMQ. Cela garantit "écriture BD ⇔ publication"
//               (atomicité), évitant qu'une commande existe sans son événement.
// QUI CONSOMME: OrderProcessor (étape 2), qui démarre la "grace period" (délai pendant
//               lequel le client peut encore annuler avant traitement).
// EFFET       : déclenche la suite de la chorégraphie.
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record OrderStatusChangedToSubmittedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required string OrderStatus { get; init; }
}
