using Domain;
using Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services;

public sealed class BidOrderingService : IBidOrderingService
{
    private readonly Dictionary<string, long> _perAuctionSeq = new();

    public Task<long> GetNextBidSequenceAsync(string auctionId)
    {
        if (!_perAuctionSeq.TryGetValue(auctionId, out var s)) s = 0;
        s++;
        _perAuctionSeq[auctionId] = s;
        return Task.FromResult(s);
    }

    public Task<bool> ValidateBidOrderAsync(string auctionId, Bid bid)
    {
        // Valid if no gap or duplicate within the source region.
        if (_perAuctionSeq.TryGetValue(auctionId, out var last))
            return Task.FromResult(bid.Sequence <= last + 1);
        return Task.FromResult(bid.Sequence == 1);
    }

    public Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null)
        => Task.FromResult(Enumerable.Empty<Bid>());
}
