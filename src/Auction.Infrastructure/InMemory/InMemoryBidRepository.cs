using Domain.Model;
using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryBidRepository : IBidRepository
{
    // Key: AuctionId; Value: list of bids
    private readonly ConcurrentDictionary<Guid, List<Bid>> _bidsByAuctionId = new();

    public Task InsertAsync(Bid bid, CancellationToken ct = default)
    {
        var list = _bidsByAuctionId.GetOrAdd(bid.AuctionId, _ => new List<Bid>());
        lock (list) { list.Add(bid); }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid auctionId, Region sourceRegionId, long sequence, CancellationToken ct = default)
    {
        if (!_bidsByAuctionId.TryGetValue(auctionId, out var list)) return Task.FromResult(false);
        lock (list) { return Task.FromResult(list.Any(b => b.SourceRegionId == sourceRegionId && b.Sequence == sequence)); }
    }

    public Task<IReadOnlyList<Bid>> GetAllForAuctionAsync(Guid auctionId, CancellationToken ct = default)
    {
        if (!_bidsByAuctionId.TryGetValue(auctionId, out var list)) return Task.FromResult<IReadOnlyList<Bid>>(Array.Empty<Bid>());
        lock (list) { return Task.FromResult<IReadOnlyList<Bid>>(list.ToList()); }
    }
}
