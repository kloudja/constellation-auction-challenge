using Domain;

namespace Infrastructure;

public interface IReconciliationCheckpointRepository
{
    Task<ReconciliationCpRecord?> GetAsync(Guid auctionId, CancellationToken ct = default);
    Task UpsertAsync(Guid auctionId, Guid? lastEventId, DateTime lastRunAtUtc, CancellationToken ct = default);
}
