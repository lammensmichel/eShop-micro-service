using Microsoft.EntityFrameworkCore;
using eShop.IntegrationEvents.Messaging;

namespace Ordering.API.Infrastructure.Outbox;

// Implémentation du journal d'événements d'intégration adossée à OrderingDbContext.
public class IntegrationEventLogService : IIntegrationEventLogService
{
    // Seuil de tentatives au-delà duquel on abandonne la republication d'une entrée.
    // Une entrée qui échoue en boucle (event « poison » côté émission : type introuvable,
    // contenu indésérialisable, broker refusant systématiquement...) ne doit pas être
    // retentée à l'infini : elle gaspillerait chaque cycle de poll et brouillerait les logs.
    // Au-delà du seuil on cesse de la SÉLECTIONNER comme « en attente » (elle reste en base à
    // l'état Failed pour inspection manuelle), ce qui ne nécessite aucune nouvelle colonne.
    public const int MaxSendAttempts = 5;

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
            .Where(e => (e.State == IntegrationEventState.NotPublished
                      || e.State == IntegrationEventState.Failed)
                      // On ÉCARTE les entrées ayant épuisé leurs tentatives : au-delà du
                      // seuil, on ne les re-sélectionne plus -> plus de retry infini. Elles
                      // demeurent en base (Failed) pour diagnostic / reprise manuelle.
                      && e.TimesSent < MaxSendAttempts)
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
