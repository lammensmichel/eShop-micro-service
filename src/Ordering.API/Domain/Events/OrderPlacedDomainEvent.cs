using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.Events;

// Domain event « commande passée », levé par le constructeur de l'agrégat Order.
// Modélisé en record immuable : un event décrit un FAIT PASSÉ, il ne se modifie pas.
// Il transporte l'agrégat concerné pour que les handlers (Application/Commands) puissent
// réagir — ici, traduire ce fait interne en integration event sortant (outbox).
public record OrderPlacedDomainEvent(Order Order) : IDomainEvent;