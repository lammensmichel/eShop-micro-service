using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Infrastructure.Repositories;

// IMPLÉMENTATION concrète du repository d'agrégat Order. L'INTERFACE IRepository<Order> vit
// dans le domaine (Domain/SeedWork) ; la mise en œuvre EF Core vit ici, dans l'infrastructure :
// c'est l'INVERSION DE DÉPENDANCE en action (le domaine ne connaît pas Postgres). Enregistrée
// dans Program.cs (AddScoped). Le repository est une fine façade au-dessus du DbContext
// (lui-même Unit of Work) : il restreint l'accès aux opérations légitimes sur l'agrégat.
public class OrderRepository : IRepository<Order>
{
    private readonly OrderingDbContext _context;

    public OrderRepository(OrderingDbContext context, IMediator mediator)
    {
        _context = context;
        // Le DbContext étant mis en pool, on ne peut pas lui injecter IMediator par son
        // constructeur ; on le renseigne donc ici, à la construction du repository (lui scoped),
        // pour activer le dispatch automatique des domain events dans SaveChangesAsync.
        _context.Mediator = mediator;
    }

    // Charge l'agrégat COMPLET : Include force le chargement des OrderItem pour que l'Order
    // reconstruit soit cohérent (frontière d'agrégat = unité de chargement). On modifie
    // ensuite l'agrégat via ses méthodes métier, jamais les lignes directement.
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