using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;
using Ordering.API.Infrastructure.Idempotency;

namespace Ordering.API.Infrastructure;

public class OrderingDbContext : DbContext
{
    // IMediator est injecté par propriété (et non par constructeur) car le DbContext
    // est mis en pool par Aspire (AddNpgsqlDbContext) : le pooling exige un constructeur
    // unique acceptant uniquement DbContextOptions. La propriété est renseignée par le
    // pipeline de dispatch (point 5) ; elle reste nulle au design-time (migrations).
    public IMediator? Mediator { get; set; }

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.OwnsOne(o => o.Address);
            order.Property(o => o.Status)
                .HasConversion(
                    s => s.Id,
                    id => id == 1 ? OrderStatus.Submitted :
                          id == 2 ? OrderStatus.AwaitingValidation :
                          id == 3 ? OrderStatus.Shipped :
                          OrderStatus.Cancelled);
            order.HasMany(o => o.OrderItems)
                .WithOne()
                .HasForeignKey("OrderId");
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.Property(i => i.UnitPrice)
                .HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<ProcessedIntegrationEvent>(e =>
        {
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).ValueGeneratedNever();
        });
    }

    // Dispatch automatique des domain events (point 5) :
    // on persiste d'abord (commit), puis on publie les events des entités suivies,
    // et enfin on nettoie. Le dispatch manuel dans les handlers est supprimé.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);

        if (Mediator is not null)
            await DispatchDomainEventsAsync(cancellationToken);

        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var entitiesWithEvents = ChangeTracker
            .Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count != 0)
            .ToList();

        // On capture puis on nettoie avant de publier pour éviter toute ré-émission
        // si un handler déclenche à son tour un SaveChangesAsync.
        var domainEvents = entitiesWithEvents.SelectMany(e => e.DomainEvents).ToList();
        foreach (var entity in entitiesWithEvents)
            entity.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
            await Mediator!.Publish(domainEvent, cancellationToken);
    }
}
