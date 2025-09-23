using Domain.Events;
using Domain.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Abstractions;

public interface IAuctionRepository
{
    Task<Auction?> GetAsync(Guid id, bool forUpdate = false, CancellationToken ct = default);
    Task InsertAsync(Auction auction, CancellationToken ct = default);

    /// <summary>Optimistic update of CurrentHighBid/CurrentSeq guarded by RowVersion.</summary>
    Task<bool> TryUpdateAmountsAsync(Guid id, decimal newHigh, long newSeq, long expectedRowVersion, CancellationToken ct = default);

    Task SaveWinnerAsync(Guid auctionId, Guid? winnerBidId, CancellationToken ct = default);
}

public interface IBidRepository
{
    Task InsertAsync(Bid bid, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid auctionId, Region sourceRegionId, long sequence, CancellationToken ct = default);
    Task<IReadOnlyList<Bid>> GetAllForAuctionAsync(Guid auctionId, CancellationToken ct = default);
}

public interface IEventOutboxRepository
{
    Task EnqueueAsync(Guid outboxId, Guid eventId, Guid auctionId, string aggregateType, string eventType, string payloadJson, DateTime createdAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxRow>> DequeuePendingAsync(int batchSize, CancellationToken ct = default);
    Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken ct = default);
}

public sealed record OutboxRow(Guid Id, Guid EventId, Guid AuctionId, string AggregateType, string EventType, string PayloadJson, DateTime CreatedAtUtc, bool Published, DateTime? PublishedAtUtc);

public interface IEventStoreRepository
{
    Task AppendAsync(EventEnvelope e, CancellationToken ct = default);
    Task<DateTime?> ResolveCreatedAtAsync(Guid? lastEventId, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> QuerySinceAsync(Guid auctionId, DateTime? sinceUtc, CancellationToken ct = default);
}

public interface IAppliedEventRepository
{
    Task<bool> IsAppliedAsync(Guid eventId, CancellationToken ct = default);
    Task MarkAppliedAsync(Guid eventId, DateTime appliedAtUtc, CancellationToken ct = default);
}

public interface IReconciliationCheckpointRepository
{
    Task<ReconciliationCp?> GetAsync(Guid auctionId, CancellationToken ct = default);
    Task UpsertAsync(Guid auctionId, Guid? lastEventId, DateTime lastRunAtUtc, CancellationToken ct = default);
}
public sealed record ReconciliationCp(Guid AuctionId, Guid? LastEventId, DateTime? LastRunAtUtc);
