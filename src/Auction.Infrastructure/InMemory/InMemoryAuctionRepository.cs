using Domain.Model;
using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryAuctionRepository : IAuctionRepository
{
    private readonly ConcurrentDictionary<Guid, Auction> _auctionDict = new();

    public Task<Auction?> GetAsync(Guid id, bool forUpdate = false, CancellationToken ct = default)
        => Task.FromResult(_auctionDict.TryGetValue(id, out var a) ? a : null);

    public Task InsertAsync(Auction auction, CancellationToken ct = default)
    {
        _auctionDict[auction.Id] = auction;
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAmountsAsync(Guid id, decimal newHigh, long newSeq, long expectedRowVersion, CancellationToken ct = default)
    {
        return Task.FromResult(_auctionDict.AddOrUpdate(id,
            _ => null!,
            (_, cur) =>
            {
                if (cur.RowVersion != expectedRowVersion) return cur; 
                cur.CurrentHighBid = newHigh;
                cur.CurrentSeq = newSeq;
                cur.RowVersion += 1;
                cur.UpdatedAtUtc = DateTime.UtcNow;
                return cur;
            })!.RowVersion == expectedRowVersion + 1);
    }

    public Task SaveWinnerAsync(Guid auctionId, Guid? winnerBidId, CancellationToken ct = default)
    {
        if (_auctionDict.TryGetValue(auctionId, out var a))
        {
            a.WinnerBidId = winnerBidId;
            a.UpdatedAtUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }
}
