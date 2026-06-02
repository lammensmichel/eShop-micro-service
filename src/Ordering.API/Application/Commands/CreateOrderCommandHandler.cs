using MediatR;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Application.Commands;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IMediator _mediator;

    public CreateOrderCommandHandler(IRepository<Order> orderRepository, IMediator mediator)
    {
        _orderRepository = orderRepository;
        _mediator = mediator;
    }

    public async Task<int> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var address = new Address(
            request.Street,
            request.City,
            request.Country,
            request.ZipCode);

        var items = request.Items.Select(i =>
            new OrderItem(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity)
        ).ToList();

        var order = new Order(request.BuyerId, address, items);

        _orderRepository.Add(order);
        await _orderRepository.SaveChangesAsync();

        // Dispatch les Domain Events
        foreach (var domainEvent in order.DomainEvents)
            await _mediator.Publish(domainEvent, cancellationToken);

        order.ClearDomainEvents();

        return order.Id;
    }
}