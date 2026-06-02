using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Levé par l'agrégat Order lorsqu'une commande est annulée.
public record OrderCancelledDomainEvent(Order Order) : IDomainEvent;
