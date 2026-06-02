using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Levé par l'agrégat Order lorsque le stock est confirmé (StockConfirmed).
public record OrderStockConfirmedDomainEvent(Order Order) : IDomainEvent;
