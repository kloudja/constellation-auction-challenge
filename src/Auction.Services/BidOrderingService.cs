using Domain;
using Domain.Abstractions;
using Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services;

public sealed class BidOrderingService : IBidOrderingService
{
    private readonly Dictionary<string, long> _perAuctionSeq = new();
    private readonly IBidRepository _bidRepository;

    public BidOrderingService(IBidRepository bidRepository)
    {
        _bidRepository = bidRepository ?? throw new ArgumentNullException(nameof(bidRepository));
    }

    public Task<long> GetNextBidSequenceAsync(string auctionId)
    {
        if (string.IsNullOrWhiteSpace(auctionId))
            throw new ArgumentException("AuctionId must be a non-empty string.", nameof(auctionId));

        if (!_perAuctionSeq.TryGetValue(auctionId, out long lastSequence))
            lastSequence = 0;

        long next = lastSequence + 1;
        _perAuctionSeq[auctionId] = next;
        return Task.FromResult(next);
    }

    public Task<bool> ValidateBidOrderAsync(string auctionId, Bid bid)
    {
        if (bid is null) throw new ArgumentNullException(nameof(bid));

        if (_perAuctionSeq.TryGetValue(auctionId, out long last))
            return Task.FromResult(bid.Sequence <= last + 1);

        return Task.FromResult(bid.Sequence == 1);
    }

    public async Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null)
    {
        if (!Guid.TryParse(auctionId, out Guid parsedAuctionId))
            throw new ArgumentException("Invalid AuctionId format.", nameof(auctionId));

        IReadOnlyList<Bid> bids = await _bidRepository.GetAllForAuctionAsync(parsedAuctionId);

        if (since is not null)
            bids = bids.Where(b => b.CreatedAtUtc >= since.Value).ToList();

        // Deterministic order aligned with reconciliation:
        // Amount desc → CreatedAtUtc asc → SourceRegionId asc → Id asc
        return bids
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.SourceRegionId)
            .ThenBy(b => b.Id)
            .ToList();
    }
}
