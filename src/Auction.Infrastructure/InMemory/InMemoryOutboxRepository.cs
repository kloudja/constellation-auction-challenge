using Domain;
using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryOutboxRepository : IEventOutboxRepository
{
    private readonly ConcurrentDictionary<Guid, OutboxRowRecord> _outboxRows = new();

    public Task EnqueueAsync(Guid outboxId, Guid eventId, Guid auctionId, string aggregateType, string eventType, string payloadJson, DateTime createdAtUtc, CancellationToken ct = default)
    {
        _outboxRows[outboxId] = new OutboxRowRecord(outboxId, eventId, auctionId, aggregateType, eventType, payloadJson, createdAtUtc, false, null);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxRowRecord>> DequeuePendingAsync(int batchSize, CancellationToken ct = default)
    {
        var pending = _outboxRows.Values.Where(r => !r.Published).OrderBy(r => r.CreatedAtUtc).Take(batchSize).ToList();
        return Task.FromResult<IReadOnlyList<OutboxRowRecord>>(pending);
    }

    public Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken ct = default)
    {
        if (_outboxRows.TryGetValue(outboxId, out var r))
            _outboxRows[outboxId] = r with { Published = true, PublishedAtUtc = publishedAtUtc };
        return Task.CompletedTask;
    }
}
