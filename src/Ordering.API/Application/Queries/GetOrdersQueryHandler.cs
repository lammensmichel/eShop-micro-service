using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Infrastructure;

namespace Ordering.API.Application.Queries;

public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, List<OrderViewModel>>
{
    private readonly OrderingDbContext _context;

    public GetOrdersQueryHandler(OrderingDbContext context)
    {
        _context = context;
    }

    public async Task<List<OrderViewModel>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
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