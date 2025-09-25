using Domain.Events;
using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryEventStoreRepository : IEventStoreRepository
{
    private readonly ConcurrentDictionary<Guid, EventEnvelope> _eventsById = new();
    private readonly ConcurrentDictionary<Guid, List<EventEnvelope>> _eventsByAuction = new();

    public Task AppendAsync(EventEnvelope e, CancellationToken ct = default)
    {
        _eventsById[e.EventId] = e;
        var list = _eventsByAuction.GetOrAdd(e.AuctionId, _ => new List<EventEnvelope>());
        lock (list) { list.Add(e); }
        return Task.CompletedTask;
    }

    public Task<DateTime?> ResolveCreatedAtAsync(Guid? lastEventId, CancellationToken ct = default)
        => Task.FromResult(lastEventId is null ? (DateTime?)null : _eventsById.TryGetValue(lastEventId.Value, out var e) ? e.CreatedAtUtc : null);

    public Task<IReadOnlyList<EventEnvelope>> QuerySinceAsync(Guid auctionId, DateTime? sinceUtc, CancellationToken ct = default)
    {
        if (!_eventsByAuction.TryGetValue(auctionId, out var list)) return Task.FromResult<IReadOnlyList<EventEnvelope>>(Array.Empty<EventEnvelope>());
        lock (list)
        {
            var q = list.AsEnumerable();
            if (sinceUtc is not null) q = q.Where(e => e.CreatedAtUtc > sinceUtc);
            q = q.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.ProducerRegionId, StringComparer.Ordinal).ThenBy(e => e.EventId);
            return Task.FromResult<IReadOnlyList<EventEnvelope>>(q.ToList());
        }
    }
}
