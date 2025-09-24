using System;
using System.Threading.Tasks;
using Domain;
using Domain.Abstractions;
using System.Collections.Concurrent;
using System.Threading;
using Domain.Model;

namespace Infrastructure.InMemory
{
    /// <summary>
    /// Lag-simulating read replica: caches snapshots and refreshes only after a configured lag.
    /// </summary>
    public sealed class InMemoryLaggedAuctionReplica : IAuctionReadReplica
    {
        private readonly IAuctionRepository _auctionRepository;
        private readonly TimeSpan _lag;
        private readonly bool _coldStart;
        private readonly ConcurrentDictionary<Guid, (Auction? Snapshot, DateTime LastRefreshUtc)> _cache = new();

        public InMemoryLaggedAuctionReplica(
            IAuctionRepository auctionRepository,
            TimeSpan lag,
            bool coldStart = false)
        {
            _auctionRepository = auctionRepository;
            _lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
            _coldStart = coldStart;
        }

        public async Task<Auction?> GetFromReplicaAsync(Guid id, CancellationToken ct = default)
        {
            DateTime now = DateTime.UtcNow;

            if (_cache.TryGetValue(id, out var entry))
            {
                if (now - entry.LastRefreshUtc < _lag)
                    return entry.Snapshot;

                Auction? fresh = await _auctionRepository.GetAsync(id, forUpdate: false, ct);
                _cache[id] = (fresh, now);
                return fresh;
            }

            if (_coldStart)
                return null;

            Auction? fetched = await _auctionRepository.GetAsync(id, forUpdate: false, ct);
            _cache[id] = (fetched, now);
            return fetched;
        }

        public async Task ForceRefreshAsync(Guid id, CancellationToken ct = default)
        {
            Auction? fresh = await _auctionRepository.GetAsync(id, forUpdate: false, ct);
            _cache[id] = (fresh, DateTime.UtcNow);
        }
    }
}
