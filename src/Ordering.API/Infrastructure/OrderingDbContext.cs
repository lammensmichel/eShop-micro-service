using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;

namespace Ordering.API.Infrastructure;

public class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

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
    }
}