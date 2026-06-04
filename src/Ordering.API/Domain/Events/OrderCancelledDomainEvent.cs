using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Domain event « commande annulée », levé par Order.Cancel().
// Même patron que OrderPlacedDomainEvent (à lire en premier) : record immuable décrivant
// un fait métier passé, transportant l'agrégat concerné. Son handler
// (OrderCancelledDomainEventHandler) ne fait ici que journaliser — tous les domain events
// n'ont pas besoin de produire un integration event sortant.
public record OrderCancelledDomainEvent(Order Order) : IDomainEvent;
