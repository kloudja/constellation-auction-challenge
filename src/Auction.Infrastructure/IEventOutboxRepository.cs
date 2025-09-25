using Domain;

namespace Infrastructure;

public interface IEventOutboxRepository
{
    Task EnqueueAsync(Guid outboxId, Guid eventId, Guid auctionId, string aggregateType, string eventType, string payloadJson, DateTime createdAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxRowRecord>> DequeuePendingAsync(int batchSize, CancellationToken ct = default);
    Task MarkPublishedAsync(Guid outboxId, DateTime publishedAtUtc, CancellationToken ct = default);
}
