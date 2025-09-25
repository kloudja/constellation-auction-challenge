using System.Collections.Concurrent;
using Domain.Model;

namespace Infrastructure.InMemory;

public sealed class InMemoryLaggedAuctionReplica(
    IAuctionRepository auctionRepository,
    TimeSpan lag,
    bool coldStart = false) : IAuctionReadReplica
{
    private readonly IAuctionRepository _auctionRepository = auctionRepository;
    private readonly TimeSpan _lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
    private readonly bool _coldStart = coldStart;
    private readonly ConcurrentDictionary<Guid, (Auction? Snapshot, DateTime LastRefreshUtc)> _cache = new();

    public async Task<Auction?> GetFromReplicaAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (_cache.TryGetValue(id, out var entry))
        {
            if (now - entry.LastRefreshUtc < _lag)
                return entry.Snapshot;

            var fresh = await _auctionRepository.GetAsync(id, forUpdate: false, ct).ConfigureAwait(false);
            var snap = fresh is null ? null : Clone(fresh);
            _cache[id] = (snap, now);
            return snap;
        }

        if (_coldStart)
            return null;

        var fetched = await _auctionRepository.GetAsync(id, forUpdate: false, ct).ConfigureAwait(false);
        var firstSnap = fetched is null ? null : Clone(fetched);
        _cache[id] = (firstSnap, now);
        return firstSnap;
    }

    public async Task ForceRefreshAsync(Guid id, CancellationToken ct = default)
    {
        var fresh = await _auctionRepository.GetAsync(id, forUpdate: false, ct).ConfigureAwait(false);
        var snap = fresh is null ? null : Clone(fresh);
        _cache[id] = (snap, DateTime.UtcNow);
    }

    private static Auction Clone(Auction a) => new()
    {
        Id = a.Id,
        OwnerRegionId = a.OwnerRegionId,
        State = a.State,
        EndsAtUtc = a.EndsAtUtc,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
        CurrentHighBid = a.CurrentHighBid,
        CurrentSeq = a.CurrentSeq,
        RowVersion = a.RowVersion,
        WinnerBidId = a.WinnerBidId,
        DeletedAtUtc = a.DeletedAtUtc
    };
}
