using MediatR;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;
using Ordering.API.Infrastructure.Idempotency;
using Ordering.API.Infrastructure.Outbox;

namespace Ordering.API.Infrastructure;

// UNIT OF WORK + mapping de persistance du service. Un DbContext EF Core EST un Unit of Work :
// il suit les changements des entités chargées et les écrit en bloc au SaveChangesAsync.
// Deux responsabilités tutorielles à retenir ici :
//   1) OnModelCreating : comment les concepts DDD se traduisent en schéma relationnel
//      (value object « owned », enumeration class convertie en entier, clé étrangère shadow) ;
//   2) override de SaveChangesAsync : le DISPATCH des domain events est centralisé ici, ce
//      qui évite de le répéter dans chaque handler (voir CreateOrderCommandHandler).
public class OrderingDbContext : DbContext
{
    // IMediator est injecté par propriété (et non par constructeur) car le DbContext
    // est mis en pool par Aspire (AddNpgsqlDbContext) : le pooling exige un constructeur
    // unique acceptant uniquement DbContextOptions. La propriété est renseignée par
    // OrderRepository à sa construction ; elle reste nulle au design-time (migrations).
    public IMediator? Mediator { get; set; }

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();
    public DbSet<IntegrationEventLogEntry> IntegrationEventLogs => Set<IntegrationEventLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            // Address est un VALUE OBJECT « possédé » (owned type) : pas de table ni de clé
            // propre, ses colonnes sont aplaties dans la table Orders. Son cycle de vie est
            // entièrement lié à l'Order qui le contient.
            order.OwnsOne(o => o.Address);
            // OrderStatus est une enumeration class : on persiste son Id (entier stable) et
            // on re-mappe l'entier vers le SINGLETON correspondant à la lecture, pour que
            // l'égalité par référence/valeur reste cohérente côté domaine.
            // NB : HasConversion attend des ARBRES D'EXPRESSION (Expression<Func<...>>), qui
            // n'autorisent ni `switch` ni `throw`. On déporte donc le mapping fail-fast dans
            // une méthode statique (un APPEL de méthode, lui, est permis dans une expression).
            order.Property(o => o.Status)
                .HasConversion(
                    s => s.Id,
                    id => StatusFromId(id));
            // Relation 1-N vers les lignes, avec une clé étrangère SHADOW « OrderId »
            // (pas de propriété OrderId dans OrderItem) : la ligne ne référence pas son
            // parent dans le modèle, l'agrégat est toujours navigué depuis Order vers ses items.
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

        // Journal d'événements d'intégration (outbox). La clé EventId est l'Id de
        // l'événement, généré côté applicatif -> ValueGeneratedNever, comme l'idempotence.
        modelBuilder.Entity<IntegrationEventLogEntry>(e =>
        {
            e.HasKey(l => l.EventId);
            e.Property(l => l.EventId).ValueGeneratedNever();
            e.Property(l => l.Content).IsRequired();
            e.Property(l => l.EventTypeName).IsRequired();
            e.Property(l => l.RoutingKey).IsRequired();
            // L'enum d'état est stocké en entier (valeurs explicites NotPublished=0...).
            e.Property(l => l.State).HasConversion<int>();
        });
    }

    // Re-mappe l'Id stocké en base vers le SINGLETON OrderStatus correspondant.
    // Cas par défaut EXPLICITE : tout Id inconnu (donnée corrompue, valeur d'une version
    // future non gérée...) lève au lieu d'être silencieusement mappé sur un statut arbitraire.
    // On préfère ÉCHOUER FRANCHEMENT (fail fast) pour que le bug soit visible plutôt que
    // de faire apparaître, par exemple, un ordre comme « annulé » à tort.
    private static OrderStatus StatusFromId(int id) => id switch
    {
        1 => OrderStatus.Submitted,
        2 => OrderStatus.AwaitingValidation,
        3 => OrderStatus.Shipped,
        4 => OrderStatus.Cancelled,
        5 => OrderStatus.StockConfirmed,
        6 => OrderStatus.Paid,
        _ => throw new ArgumentOutOfRangeException(
            nameof(id), id, "OrderStatus Id inconnu lu en base")
    };

    // DISPATCH AUTOMATIQUE DES DOMAIN EVENTS sur SaveChanges. Ordre :
    //   1) base.SaveChangesAsync : EF écrit les changements en base (INSERT/UPDATE) et, pour
    //      une entité neuve, AFFECTE son Id généré -> les handlers peuvent l'utiliser ;
    //   2) on publie ensuite les events des entités suivies via MediatR.
    // Nuance importante : « écrit en base » ne veut PAS dire « committé ». Quand le flux vient
    // du RabbitMQConsumer, ce SaveChanges s'exécute DANS une transaction ouverte plus haut ;
    // les handlers (qui déposent des entrées outbox) écrivent donc dans la MÊME transaction,
    // et tout est committé ensemble par le consumer. C'est ce qui rend « changement métier +
    // outbox » atomique : si un handler échoue, la transaction entière est annulée.
    // Faire le dispatch ici (et non manuellement dans chaque handler) garantit qu'il a TOUJOURS
    // lieu, sans duplication.
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
