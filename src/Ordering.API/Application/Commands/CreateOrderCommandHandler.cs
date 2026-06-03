using MediatR;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Application.Commands;

// Handler de commande : orchestre le cas d'usage SANS contenir de règle métier.
// Son rôle se limite à traduire le DTO en objets du domaine, à déléguer la logique à
// l'agrégat (qui porte les invariants) et à persister via le repository. La logique
// reste ainsi dans le domaine, pas dans la couche application (DDD).
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

        // C'est le constructeur de l'agrégat qui applique les invariants ET lève
        // OrderPlacedDomainEvent : la couche application n'a aucune règle à dupliquer.
        var order = new Order(request.BuyerId, address, items);

        _orderRepository.Add(order);

        // Le dispatch des Domain Events est désormais automatique :
        // il est déclenché par l'override de SaveChangesAsync du OrderingDbContext.
        await _orderRepository.SaveChangesAsync();

        return order.Id;
    }
}