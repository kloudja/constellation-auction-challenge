using Domain.Events;
using Domain.Abstractions;
using Domain.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.InMemory;

/// <summary>Thread-safe in-memory infra to unblock tests and end-to-end flows.</summary>
public sealed class InMemoryAuctionRepository : IAuctionRepository
{
    private readonly ConcurrentDictionary<Guid, Auction> _mem = new();

    public Task<Auction?> GetAsync(Guid id, bool forUpdate = false, CancellationToken ct = default)
        => Task.FromResult(_mem.TryGetValue(id, out var a) ? a : null);

    public Task InsertAsync(Auction auction, CancellationToken ct = default)
    {
        _mem[auction.Id] = auction;
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAmountsAsync(Guid id, decimal newHigh, long newSeq, long expectedRowVersion, CancellationToken ct = default)
    {
        return Task.FromResult(_mem.AddOrUpdate(id,
            _ => null!, // shouldn't happen, assume exists
            (_, cur) =>
            {
                if (cur.RowVersion != expectedRowVersion) return cur; // reject
                cur.CurrentHighBid = newHigh;
                cur.CurrentSeq = newSeq;
                cur.RowVersion += 1;
                cur.UpdatedAtUtc = DateTime.UtcNow;
                return cur;
            })!.RowVersion == expectedRowVersion + 1);
    }

    public Task SaveWinnerAsync(Guid auctionId, Guid? winnerBidId, CancellationToken ct = default)
    {
        if (_mem.TryGetValue(auctionId, out var a))
        {
            a.WinnerBidId = winnerBidId;
            a.UpdatedAtUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryBidRepository : IBidRepository
{
    // Key: AuctionId; Value: list of bids
    private readonly ConcurrentDictionary<Guid, List<Bid>> _mem = new();

    public Task InsertAsync(Bid bid, CancellationToken ct = default)
    {
        var list = _mem.GetOrAdd(bid.AuctionId, _ => new List<Bid>());
        lock (list) { list.Add(bid); }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid auctionId, Region sourceRegionId, long sequence, CancellationToken ct = default)
    {
        if (!_mem.TryGetValue(auctionId, out var list)) return Task.FromResult(false);
        lock (list) { return Task.FromResult(list.Any(b => b.SourceRegionId == sourceRegionId && b.Sequence == sequence)); }
    }

    public Task<IReadOnlyList<Bid>> GetAllForAuctionAsync(Guid auctionId, CancellationToken ct = default)
    {
        if (!_mem.TryGetValue(auctionId, out var list)) return Task.FromResult<IReadOnlyList<Bid>>(Array.Empty<Bid>());
        lock (list) { return Task.FromResult<IReadOnlyList<Bid>>(list.ToList()); }
    }
}

public sealed class InMemoryOutboxRepository : IEventOutboxRepository
{
    private readonly ConcurrentDictionary<Guid, OutboxRow> _rows = new();

    public Task EnqueueAsync(Guid outboxId, Guid eventId, Guid auctionId, string aggregateType, string eventType, string payloadJson, DateTime createdAtUtc, CancellationToken ct = default)
    {
        _rows[outboxId] = new OutboxRow(outboxId, eventId, auctionId, aggregateType, eventType, payloadJson, createdAtUtc, false, null);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxRow>> DequeuePendingAsync(int batchSize, CancellationToken ct = default)
    {
        var pending = _rows.Values.Where(r => !r.Published).OrderBy(r => r.CreatedAtUtc).Take(batchSize).ToList();
        return Task.FromResult<IReadOnlyList<OutboxRow>>(pending);
    }

    public Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken ct = default)
    {
        if (_rows.TryGetValue(outboxId, out var r))
            _rows[outboxId] = r with { Published = true, PublishedAtUtc = publishedAtUtc };
        return Task.CompletedTask;
    }
}

public sealed class InMemoryEventStoreRepository : IEventStoreRepository
{
    private readonly ConcurrentDictionary<Guid, EventEnvelope> _byId = new();
    private readonly ConcurrentDictionary<Guid, List<EventEnvelope>> _byAuction = new();

    public Task AppendAsync(EventEnvelope e, CancellationToken ct = default)
    {
        _byId[e.EventId] = e;
        var list = _byAuction.GetOrAdd(e.AuctionId, _ => new List<EventEnvelope>());
        lock (list) { list.Add(e); }
        return Task.CompletedTask;
    }

    public Task<DateTime?> ResolveCreatedAtAsync(Guid? lastEventId, CancellationToken ct = default)
        => Task.FromResult(lastEventId is null ? (DateTime?)null : _byId.TryGetValue(lastEventId.Value, out var e) ? e.CreatedAtUtc : null);

    public Task<IReadOnlyList<EventEnvelope>> QuerySinceAsync(Guid auctionId, DateTime? sinceUtc, CancellationToken ct = default)
    {
        if (!_byAuction.TryGetValue(auctionId, out var list)) return Task.FromResult<IReadOnlyList<EventEnvelope>>(Array.Empty<EventEnvelope>());
        lock (list)
        {
            var q = list.AsEnumerable();
            if (sinceUtc is not null) q = q.Where(e => e.CreatedAtUtc > sinceUtc);
            q = q.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.ProducerRegionId).ThenBy(e => e.EventId);
            return Task.FromResult<IReadOnlyList<EventEnvelope>>(q.ToList());
        }
    }
}

public sealed class InMemoryAppliedEventRepository : IAppliedEventRepository
{
    private readonly ConcurrentDictionary<Guid, DateTime> _applied = new();
    public Task<bool> IsAppliedAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(_applied.ContainsKey(eventId));
    public Task MarkAppliedAsync(Guid eventId, DateTime appliedAtUtc, CancellationToken ct = default)
    { _applied[eventId] = appliedAtUtc; return Task.CompletedTask; }
}

public sealed class InMemoryReconciliationCheckpointRepository : IReconciliationCheckpointRepository
{
    private readonly ConcurrentDictionary<Guid, ReconciliationCp> _mem = new();
    public Task<ReconciliationCp?> GetAsync(Guid auctionId, CancellationToken ct = default)
        => Task.FromResult(_mem.TryGetValue(auctionId, out var cp) ? cp : null);

    public Task UpsertAsync(Guid auctionId, Guid? lastEventId, DateTime lastRunAtUtc, CancellationToken ct = default)
    {
        _mem[auctionId] = new ReconciliationCp(auctionId, lastEventId, lastRunAtUtc);
        return Task.CompletedTask;
    }
}
