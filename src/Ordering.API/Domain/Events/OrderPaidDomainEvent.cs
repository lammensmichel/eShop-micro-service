using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Levé par l'agrégat Order lorsque le paiement est confirmé (Paid).
public record OrderPaidDomainEvent(Order Order) : IDomainEvent;
