using Domain.Model;

namespace Infrastructure;

public interface IAuctionReadReplica
{
    Task<Auction?> GetFromReplicaAsync(Guid id, CancellationToken ct = default);
}
