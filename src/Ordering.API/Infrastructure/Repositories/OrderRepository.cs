using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Infrastructure.Repositories;

public class OrderRepository : IRepository<Order>
{
    private readonly OrderingDbContext _context;

    public OrderRepository(OrderingDbContext context, IMediator mediator)
    {
        _context = context;
        // Renseigne le médiateur sur le contexte (mis en pool) pour activer
        // le dispatch automatique des domain events lors de SaveChangesAsync (point 5).
        _context.Mediator = mediator;
    }

    public async Task<Order?> GetAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public Order Add(Order aggregate)
    {
        return _context.Orders.Add(aggregate).Entity;
    }

    public void Update(Order aggregate)
    {
        _context.Orders.Update(aggregate);
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
}