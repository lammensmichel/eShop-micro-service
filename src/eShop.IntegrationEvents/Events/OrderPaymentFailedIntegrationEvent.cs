using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°5b — BRANCHE ÉCHEC (terminale). Voir le schéma complet en
// tête de BasketCheckoutEvent.cs.
//
// QUI ÉMET     : PaymentProcessor (étape 2) lorsque le paiement ÉCHOUE.
// ROUTING KEY  : "payment-failed".
// QUI CONSOMME : Ordering.API, qui ANNULE la commande (transition vers Cancelled).
// C'est le "chemin de compensation" de la saga : comme il n'y a pas de transaction
// distribuée unique sur plusieurs services, on ne fait pas de ROLLBACK ; on émet un
// événement qui ramène la commande à un état cohérent (annulée). Pendant "succès" :
// OrderPaymentSucceededIntegrationEvent.
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record OrderPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }

    // Motif d'échec du paiement (optionnel), à des fins de journalisation/diagnostic.
    public string? Reason { get; init; }
}
