using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

public record OrderPlacedDomainEvent(Order Order) : IDomainEvent;