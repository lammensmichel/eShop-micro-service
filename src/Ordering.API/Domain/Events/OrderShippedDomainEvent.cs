using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Levé par l'agrégat Order lorsqu'une commande est expédiée.
public record OrderShippedDomainEvent(Order Order) : IDomainEvent;
