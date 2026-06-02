using MediatR;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Application.Commands;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
{
    private readonly IRepository<Order> _orderRepository;

    public CreateOrderCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
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

        // Le dispatch des Domain Events est désormais automatique :
        // il est déclenché par l'override de SaveChangesAsync du OrderingDbContext.
        await _orderRepository.SaveChangesAsync();

        return order.Id;
    }
}