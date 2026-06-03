using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Infrastructure;

namespace Ordering.API.Application.Queries;

// Handler de lecture : interroge DIRECTEMENT le DbContext (pas le repository d'agrégat)
// et projette vers des ViewModels. Le côté lecture du CQRS peut ainsi s'autoriser des
// requêtes optimisées sans passer par la racine d'agrégat ni lever de domain events.
public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, List<OrderViewModel>>
{
    private readonly OrderingDbContext _context;

    public GetOrdersQueryHandler(OrderingDbContext context)
    {
        _context = context;
    }

    public async Task<List<OrderViewModel>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        // Filtre par BuyerId (issu du jeton, voir l'API) -> chaque acheteur ne voit
        // que SES commandes. Include charge les lignes pour calculer le total.
        var orders = await _context.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == request.BuyerId)
            .ToListAsync(cancellationToken);

        return orders.Select(o => new OrderViewModel
        {
            Id = o.Id,
            BuyerId = o.BuyerId,
            Status = o.Status.ToString(),
            Total = o.TotalPrice,
            OrderDate = o.OrderDate,
            Items = o.OrderItems.Select(i => new OrderItemViewModel
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        }).ToList();
    }
}