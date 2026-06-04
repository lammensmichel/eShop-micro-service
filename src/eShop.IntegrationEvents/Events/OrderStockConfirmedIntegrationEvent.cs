using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// MAILLON DE SAGA n°4. Voir le schéma complet en tête de BasketCheckoutEvent.cs.
//
// QUI ÉMET     : Ordering.API (via l'outbox, cf. note outbox dans
//                OrderStatusChangedToSubmittedIntegrationEvent.cs) une fois le stock
//                confirmé pour la commande.
// ROUTING KEY  : "ordering-order-stock-confirmed".
// QUI CONSOMME : PaymentProcessor, qui « charge » le paiement via une passerelle
//                (IPaymentGateway) et répond par l'un des deux événements terminaux.
// SUITE (branche) : OrderPaymentSucceededIntegrationEvent (succès)
//                   OU OrderPaymentFailedIntegrationEvent (échec).
//
// DONNÉES DE PAIEMENT : cet événement transporte désormais le MONTANT et le moyen de
// paiement, car c'est le PaymentProcessor qui réalise la transaction (il a besoin de
// quoi débiter). Le montant vient de Order.TotalPrice.
//
// ⚠️ AVERTISSEMENT PCI-DSS (important pour « un jour, un vrai paiement ») :
// transporter/stocker un numéro de carte EN CLAIR est INTERDIT en production. Ici on le
// fait UNIQUEMENT pour la simulation pédagogique. Le jour d'un vrai paiement, on
// remplacerait ces champs carte par un JETON de paiement (tokenisation chez le
// prestataire au moment du checkout) : le PAN ne transiterait alors jamais par nos
// services ni par le bus. L'abstraction IPaymentGateway côté PaymentProcessor est
// justement faite pour ce remplacement.
//
// Id (hérité de IntegrationEvent) = clé d'idempotence côté consommateur.
// =============================================================================
public record OrderStockConfirmedIntegrationEvent : IntegrationEvent
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }

    // Montant à débiter (= total de la commande).
    public required decimal Amount { get; init; }

    // Moyen de paiement (simulation). À remplacer par un jeton en production (cf. ⚠️).
    public required string CardNumber { get; init; }
    public required string CardHolderName { get; init; }
    public required DateTime CardExpiration { get; init; }
}
