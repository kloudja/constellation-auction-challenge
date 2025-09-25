using Domain.Events;

namespace Infrastructure;

public interface IEventStoreRepository
{
    Task AppendAsync(EventEnvelope e, CancellationToken ct = default);
    Task<DateTime?> ResolveCreatedAtAsync(Guid? lastEventId, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> QuerySinceAsync(Guid auctionId, DateTime? sinceUtc, CancellationToken ct = default);
}
