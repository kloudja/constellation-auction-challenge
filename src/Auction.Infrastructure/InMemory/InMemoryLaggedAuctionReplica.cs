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
            _cache[id] = (fresh, now);
            return fresh;
        }

        if (_coldStart)
            return null;

        var fetched = await _auctionRepository.GetAsync(id, forUpdate: false, ct).ConfigureAwait(false);
        _cache[id] = (fetched, now);
        return fetched;
    }

    public async Task ForceRefreshAsync(Guid id, CancellationToken ct = default)
    {
        var fresh = await _auctionRepository.GetAsync(id, forUpdate: false, ct).ConfigureAwait(false);
        _cache[id] = (fresh, DateTime.UtcNow);
    }
}
