using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Domain event « commande expédiée », levé par Order.Ship() (état terminal du parcours
// nominal). Même patron que OrderPlacedDomainEvent (à lire en premier). Son handler ne
// fait que journaliser ici, faute d'étape suivante.
public record OrderShippedDomainEvent(Order Order) : IDomainEvent;
