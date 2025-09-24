using Domain.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Domain;

/// <summary>
/// Read replica abstraction to support ConsistencyLevel.Eventual reads.
/// </summary>
public interface IAuctionReadReplica
{
    Task<Auction?> GetFromReplicaAsync(Guid id, CancellationToken ct = default);
}
