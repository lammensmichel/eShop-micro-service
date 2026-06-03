using MediatR;

namespace Ordering.API.Domain.SeedWork;

// Marqueur des DOMAIN EVENTS. À ne pas confondre avec un INTEGRATION EVENT :
//  - Domain event : INTERNE au service, synchrone, dispatché via MediatR dans le même
//    process/la même transaction. Décrit un fait métier (« OrderPlaced ») et déclenche
//    des réactions locales (logs, mise en file outbox...).
//  - Integration event : SORTANT, publié sur le bus (RabbitMQ) pour les AUTRES services,
//    de façon asynchrone et fiable (voir Outbox/). Souvent produit EN RÉACTION à un
//    domain event.
//
// IDomainEvent hérite de INotification : un même event peut donc avoir 0..n handlers
// (INotificationHandler) que MediatR appellera tous via Publish — sémantique « pub/sub »,
// contrairement à IRequest/Send qui n'a qu'un seul handler.
public interface IDomainEvent : INotification
{
}