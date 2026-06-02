using Microsoft.EntityFrameworkCore;
using eShop.IntegrationEvents.Messaging;

namespace Ordering.API.Infrastructure.Outbox;

// Implémentation du journal d'événements d'intégration adossée à OrderingDbContext.
public class IntegrationEventLogService : IIntegrationEventLogService
{
    private readonly OrderingDbContext _context;

    public IntegrationEventLogService(OrderingDbContext context)
    {
        _context = context;
    }

    public Task SaveEventAsync(IntegrationEvent evt, string routingKey)
    {
        var entry = new IntegrationEventLogEntry(evt, routingKey);

        // On ajoute simplement l'entrée au tracker : le SaveChanges qui suivra (celui
        // de ProcessEventAsync, dans la transaction ouverte) la persistera de façon
        // atomique avec la commande. On NE commite donc PAS ici.
        _context.IntegrationEventLogs.Add(entry);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<IntegrationEventLogEntry>> RetrievePendingEventsAsync()
    {
        return await _context.IntegrationEventLogs
            .Where(e => e.State == IntegrationEventState.NotPublished
                     || e.State == IntegrationEventState.Failed)
            .OrderBy(e => e.CreationTime)
            .ToListAsync();
    }

    public Task MarkAsInProgressAsync(Guid eventId) =>
        UpdateStateAsync(eventId, IntegrationEventState.InProgress);

    public Task MarkAsPublishedAsync(Guid eventId) =>
        UpdateStateAsync(eventId, IntegrationEventState.Published);

    public async Task MarkAsFailedAsync(Guid eventId)
    {
        var entry = await _context.IntegrationEventLogs.FindAsync(eventId);
        if (entry is null) return;

        entry.State = IntegrationEventState.Failed;
        entry.TimesSent++;
        await _context.SaveChangesAsync();
    }

    private async Task UpdateStateAsync(Guid eventId, IntegrationEventState state)
    {
        var entry = await _context.IntegrationEventLogs.FindAsync(eventId);
        if (entry is null) return;

        entry.State = state;
        if (state == IntegrationEventState.InProgress)
            entry.TimesSent++;

        await _context.SaveChangesAsync();
    }
}
