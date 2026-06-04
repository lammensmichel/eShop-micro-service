using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Domain event « stock confirmé », levé par Order.SetStockConfirmed().
// Même patron que OrderPlacedDomainEvent (à lire en premier). Contrairement aux events
// purement journalisés, son handler (OrderStockConfirmedDomainEventHandler) le TRADUIT en
// integration event sortant déposé dans l'outbox, pour faire avancer la saga inter-services.
public record OrderStockConfirmedDomainEvent(Order Order) : IDomainEvent;
