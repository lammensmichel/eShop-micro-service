using MediatR;

namespace Ordering.API.Application.Commands;

// Commandes pilotant le cycle de vie de l'agrégat Order (point 9).
// BuyerId est dérivé du jeton JWT côté API (anti-IDOR) : le handler vérifie
// que la commande appartient bien à l'appelant.
public record SetAwaitingValidationCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record ShipOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record CancelOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;

// Commandes pilotées par les événements de la saga (Chantier B), et non par l'utilisateur.
// Elles ne font pas de contrôle de propriété anti-IDOR (la source est le bus interne).

// Déclenchée à la réception de GracePeriodConfirmedIntegrationEvent : passe la commande
// en AwaitingValidation puis confirme le stock de façon SIMPLIFIÉE (auto-confirmée,
// pas d'appel réel à Catalog) -> SetStockConfirmed() qui lève le domain event
// enfilant OrderStockConfirmedIntegrationEvent dans l'outbox.
public record ConfirmGracePeriodCommand(int OrderId, string BuyerId) : IRequest<Unit>;

// Déclenchée à la réception de OrderPaymentSucceededIntegrationEvent : passe la commande
// en Paid puis l'expédie (Ship).
public record ConfirmOrderPaymentCommand(int OrderId, string BuyerId) : IRequest<Unit>;

// Déclenchée à la réception de OrderPaymentFailedIntegrationEvent : annule la commande.
public record CancelOrderPaymentCommand(int OrderId, string BuyerId) : IRequest<Unit>;
