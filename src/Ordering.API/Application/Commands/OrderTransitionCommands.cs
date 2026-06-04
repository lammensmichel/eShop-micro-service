using MediatR;

namespace Ordering.API.Application.Commands;

// Commandes CQRS qui pilotent les TRANSITIONS de la machine à états d'Order (voir Order.cs).
// On distingue deux familles selon QUI les déclenche.
//
// 1) Transitions PILOTÉES PAR L'UTILISATEUR (via les endpoints HTTP, voir OrderingApi.cs).
//    BuyerId est dérivé du jeton JWT côté API (anti-IDOR : Insecure Direct Object Reference) ;
//    le handler vérifie que la commande appartient bien à l'appelant, sinon il renvoie « non
//    trouvée » plutôt que « interdite » pour ne pas divulguer l'existence de la ressource.
public record SetAwaitingValidationCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record ShipOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record CancelOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;

// 2) Transitions PILOTÉES PAR LA SAGA. Une « saga » est un enchaînement de transactions
//    locales réparties sur plusieurs services, coordonnées par des integration events sur
//    le bus (il n'y a pas de transaction distribuée). Ici Ordering réagit aux events des
//    autres services (période de grâce, paiement) que le RabbitMQConsumer traduit en ces
//    commandes. Elles ne font PAS de contrôle de propriété anti-IDOR : la source n'est pas
//    un utilisateur mais le bus interne, déjà de confiance.

// Déclenchée à la réception de GracePeriodConfirmedIntegrationEvent : passe la commande
// en AwaitingValidation puis confirme le stock de façon SIMPLIFIÉE (auto-confirmée,
// pas d'appel réel à Catalog) -> SetStockConfirmed() qui lève le domain event
// enfilant OrderStockConfirmedIntegrationEvent dans l'outbox.
public record ConfirmGracePeriodCommand(int OrderId, string BuyerId) : IRequest<Unit>;

// Déclenchée à la réception de OrderPaymentSucceededIntegrationEvent : passe la commande
// en Paid puis l'expédie (Ship). TransactionId = référence du paiement renvoyée par la
// passerelle, conservée sur la commande pour la traçabilité.
public record ConfirmOrderPaymentCommand(int OrderId, string BuyerId, string TransactionId) : IRequest<Unit>;

// Déclenchée à la réception de OrderPaymentFailedIntegrationEvent : annule la commande.
public record CancelOrderPaymentCommand(int OrderId, string BuyerId) : IRequest<Unit>;
