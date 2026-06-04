using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Domain event « commande payée », levé par Order.SetPaid().
// Même patron que OrderPlacedDomainEvent (à lire en premier). Son handler se contente
// de journaliser : dans cette implémentation, la suite de la saga (l'expédition) est
// enchaînée localement par le handler de commande, sans repasser par le bus.
public record OrderPaidDomainEvent(Order Order) : IDomainEvent;
