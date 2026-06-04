using MediatR;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Application.Commands;

// Handlers des transitions du cycle de vie d'Order. Tous suivent le MÊME patron, qui résume
// l'écriture en CQRS/DDD :
//   1) charger l'agrégat COMPLET via le repository (GetAsync) ;
//   2) (transitions utilisateur) vérifier la propriété anti-IDOR ;
//   3) appeler une MÉTHODE MÉTIER de l'agrégat (Ship, Cancel...) — c'est ELLE qui valide la
//      transition (machine à états) et lève le domain event ; le handler ne décide de rien ;
//   4) Update + SaveChangesAsync.
// Le SaveChangesAsync déclenche le DISPATCH automatique des domain events (override dans
// OrderingDbContext) -> les handlers d'events s'exécutent, certains déposant un integration
// event dans l'outbox. Tout cela se passe dans la transaction ouverte par RabbitMQConsumer
// quand la commande vient de la saga -> atomicité changement métier + outbox + idempotence.

public class SetAwaitingValidationCommandHandler : IRequestHandler<SetAwaitingValidationCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public SetAwaitingValidationCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(SetAwaitingValidationCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.SetAwaitingValidation();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ShipOrderCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.Ship();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public CancelOrderCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.Cancel();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// --- Handlers des transitions pilotées par la saga (déclenchés par le RabbitMQConsumer) ---

// GracePeriodConfirmed -> AwaitingValidation puis confirmation du stock (simplifiée).
// SetStockConfirmed() lève OrderStockConfirmedDomainEvent dont le handler enfile
// l'event d'intégration sortant dans l'outbox (toujours dans la transaction du consumer).
//
// IDEMPOTENCE (point critique d'une saga). L'idempotence par EventId (table
// ProcessedIntegrationEvents) n'attrape QUE le rejeu du MÊME événement. Mais le bus garantit
// « au moins une fois » et un service amont peut ré-émettre le fait métier avec un NOUVEL
// EventId (autre instance, retry applicatif...). Dans ce cas le filtre par EventId laisse
// passer, la commande est rejouée, et un appel EN DUR à SetAwaitingValidation() lèverait
// (l'ordre est déjà StockConfirmed) -> nack -> retries -> DLQ : une commande déjà honorée
// finirait « poison ». La parade est de rendre le HANDLER idempotent en conditionnant chaque
// transition à l'état COURANT : si l'ordre a déjà dépassé l'étape, on ne fait rien (no-op).
// On NE touche PAS aux gardes du domaine (Order reste strict) : l'idempotence est une
// responsabilité de l'orchestration saga, pas de l'invariant métier.
public class ConfirmGracePeriodCommandHandler : IRequestHandler<ConfirmGracePeriodCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ConfirmGracePeriodCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ConfirmGracePeriodCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Chaque transition n'est tentée que depuis l'état attendu. Si l'ordre est déjà
        // StockConfirmed/Paid/Shipped/Cancelled, ces deux conditions sont fausses -> no-op :
        // un rejeu (nouvel EventId) est absorbé sans erreur.
        if (order.Status == OrderStatus.Submitted)
            order.SetAwaitingValidation();
        // Validation de stock simplifiée : auto-confirmée, sans appel réel à Catalog.
        if (order.Status == OrderStatus.AwaitingValidation)
            order.SetStockConfirmed();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// OrderPaymentSucceeded -> Paid puis Shipped.
public class ConfirmOrderPaymentCommandHandler : IRequestHandler<ConfirmOrderPaymentCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ConfirmOrderPaymentCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ConfirmOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Idempotence saga : même logique conditionnée par l'état courant que pour la
        // période de grâce. Si l'ordre est déjà Paid -> on ne re-paye pas ; déjà Shipped ->
        // les deux conditions sont fausses (no-op). Un rejeu avec un nouvel EventId est ainsi
        // absorbé au lieu de lever et de finir en DLQ.
        if (order.Status == OrderStatus.StockConfirmed)
            order.SetPaid();
        if (order.Status == OrderStatus.Paid)
            order.Ship();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// OrderPaymentFailed -> Cancelled.
public class CancelOrderPaymentCommandHandler : IRequestHandler<CancelOrderPaymentCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public CancelOrderPaymentCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(CancelOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Idempotence saga : on n'annule que si ce n'est pas déjà fait. Cancel() lèverait sur
        // un ordre Shipped (on ne « dé-livre » pas) et serait un travail inutile sur un ordre
        // déjà Cancelled. Tolérer le rejeu (nouvel EventId d'un OrderPaymentFailed re-livré)
        // évite de transformer une annulation déjà appliquée en message poison.
        if (order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Shipped)
            order.Cancel();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}
