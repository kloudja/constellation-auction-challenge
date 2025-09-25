using Domain;
using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryReconciliationCheckpointRepository : IReconciliationCheckpointRepository
{
    private readonly ConcurrentDictionary<Guid, ReconciliationCpRecord> _cpRecords = new();
    public Task<ReconciliationCpRecord?> GetAsync(Guid auctionId, CancellationToken ct = default)
        => Task.FromResult(_cpRecords.TryGetValue(auctionId, out var cp) ? cp : null);

    public Task UpsertAsync(Guid auctionId, Guid? lastEventId, DateTime lastRunAtUtc, CancellationToken ct = default)
    {
        _cpRecords[auctionId] = new ReconciliationCpRecord(auctionId, lastEventId, lastRunAtUtc);
        return Task.CompletedTask;
    }
}
