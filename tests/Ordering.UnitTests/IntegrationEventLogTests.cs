using Microsoft.EntityFrameworkCore;
using eShop.IntegrationEvents.Events;
using Ordering.API.Infrastructure;
using Ordering.API.Infrastructure.Outbox;
using Xunit;

namespace Ordering.UnitTests;

// Tests du pattern Outbox : round-trip de sérialisation des entrées du journal et
// transitions d'état pilotées par IntegrationEventLogService (sur base EF InMemory).
public class IntegrationEventLogTests
{
    private static OrderingDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OrderStatusChangedToSubmittedIntegrationEvent SampleEvent() => new()
    {
        OrderId = 42,
        BuyerId = "buyer-123",
        OrderStatus = "submitted"
    };

    // --- Round-trip sérialisation / désérialisation ---

    [Fact]
    public void Entree_Serialise_Levenement_Et_Le_Type()
    {
        var evt = SampleEvent();

        var entry = new IntegrationEventLogEntry(evt, "ordering-order-submitted");

        Assert.Equal(evt.Id, entry.EventId);
        Assert.Equal(evt.CreationDate, entry.CreationTime);
        Assert.Equal("ordering-order-submitted", entry.RoutingKey);
        Assert.Equal(IntegrationEventState.NotPublished, entry.State);
        Assert.Equal(0, entry.TimesSent);
        Assert.Contains("42", entry.Content);
        Assert.Contains(nameof(OrderStatusChangedToSubmittedIntegrationEvent), entry.EventTypeName);
    }

    [Fact]
    public void DeserializeJsonContent_Reconstitue_Levenement_Type()
    {
        var evt = SampleEvent();
        var entry = new IntegrationEventLogEntry(evt, "ordering-order-submitted");

        var roundTripped = entry.DeserializeJsonContent();

        var typed = Assert.IsType<OrderStatusChangedToSubmittedIntegrationEvent>(roundTripped);
        Assert.Equal(evt.Id, typed.Id);
        Assert.Equal(evt.OrderId, typed.OrderId);
        Assert.Equal(evt.BuyerId, typed.BuyerId);
        Assert.Equal(evt.OrderStatus, typed.OrderStatus);
    }

    // --- Transitions d'état via le service ---

    [Fact]
    public async Task SaveEventAsync_Ajoute_Au_Tracker_Sans_Commiter()
    {
        await using var context = CreateInMemoryContext();
        var service = new IntegrationEventLogService(context);

        await service.SaveEventAsync(SampleEvent(), "rk");

        // Le service ne fait qu'ajouter l'entrée au tracker (état Added) : il n'appelle
        // pas SaveChanges -> l'entrée participera à la transaction ambiante.
        var tracked = context.ChangeTracker.Entries<IntegrationEventLogEntry>().Single();
        Assert.Equal(EntityState.Added, tracked.State);
    }

    [Fact]
    public async Task Transition_NotPublished_InProgress_Published()
    {
        await using var context = CreateInMemoryContext();
        var service = new IntegrationEventLogService(context);
        var evt = SampleEvent();

        await service.SaveEventAsync(evt, "rk");
        await context.SaveChangesAsync(); // simule le commit transactionnel ambiant

        await service.MarkAsInProgressAsync(evt.Id);
        var afterInProgress = await context.IntegrationEventLogs.FindAsync(evt.Id);
        Assert.Equal(IntegrationEventState.InProgress, afterInProgress!.State);
        Assert.Equal(1, afterInProgress.TimesSent); // InProgress incrémente le compteur d'envois

        await service.MarkAsPublishedAsync(evt.Id);
        var afterPublished = await context.IntegrationEventLogs.FindAsync(evt.Id);
        Assert.Equal(IntegrationEventState.Published, afterPublished!.State);
    }

    [Fact]
    public async Task MarkAsFailed_Met_Failed_Et_Incremente_TimesSent()
    {
        await using var context = CreateInMemoryContext();
        var service = new IntegrationEventLogService(context);
        var evt = SampleEvent();

        await service.SaveEventAsync(evt, "rk");
        await context.SaveChangesAsync();

        await service.MarkAsFailedAsync(evt.Id);

        var entry = await context.IntegrationEventLogs.FindAsync(evt.Id);
        Assert.Equal(IntegrationEventState.Failed, entry!.State);
        Assert.Equal(1, entry.TimesSent);
    }

    [Fact]
    public async Task RetrievePendingEvents_Retourne_NotPublished_Et_Failed_Pas_Published()
    {
        await using var context = CreateInMemoryContext();
        var service = new IntegrationEventLogService(context);

        var notPublished = SampleEvent();
        var failed = SampleEvent();
        var published = SampleEvent();

        await service.SaveEventAsync(notPublished, "rk");
        await service.SaveEventAsync(failed, "rk");
        await service.SaveEventAsync(published, "rk");
        await context.SaveChangesAsync();

        await service.MarkAsFailedAsync(failed.Id);
        await service.MarkAsPublishedAsync(published.Id);

        var pending = await service.RetrievePendingEventsAsync();

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, e => e.EventId == notPublished.Id);
        Assert.Contains(pending, e => e.EventId == failed.Id);
        Assert.DoesNotContain(pending, e => e.EventId == published.Id);
    }
}
