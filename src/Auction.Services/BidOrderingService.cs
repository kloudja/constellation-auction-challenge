using Domain;
using Domain.Model;
using Infrastructure;

namespace Services;

public sealed class BidOrderingService(IBidRepository bidRepository) : IBidOrderingService
{
    private readonly Dictionary<string, long> _perAuctionSeq = new(StringComparer.Ordinal);
    private readonly IBidRepository _bidRepository = bidRepository ?? throw new ArgumentNullException(nameof(bidRepository));

    public Task<long> GetNextBidSequenceAsync(string auctionId)
    {
        if (string.IsNullOrWhiteSpace(auctionId))
            throw new ArgumentException("AuctionId must be a non-empty string.", nameof(auctionId));

        if (!_perAuctionSeq.TryGetValue(auctionId, out var lastSequence))
            lastSequence = 0;

        var next = lastSequence + 1;
        _perAuctionSeq[auctionId] = next;
        return Task.FromResult(next);
    }

    public Task<BidAcceptance> ValidateBidOrderAsync(string auctionId, Bid bid)
    {
        if (bid is null) throw new ArgumentNullException(nameof(bid));
        if (string.IsNullOrWhiteSpace(auctionId)) throw new ArgumentException("Required", nameof(auctionId));
        if (bid.Sequence <= 0) return Task.FromResult(BidAcceptance.Invalid);

        if (_perAuctionSeq.TryGetValue(auctionId, out var last))
        {
            if (bid.Sequence == last + 1) return Task.FromResult(BidAcceptance.Accepted);
            if (bid.Sequence <= last) return Task.FromResult(BidAcceptance.Duplicate);
            return Task.FromResult(BidAcceptance.OutOfOrder);
        }

        return Task.FromResult(bid.Sequence == 1
            ? BidAcceptance.Accepted
            : BidAcceptance.OutOfOrder);
    }

    public async Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null)
    {
        if (!Guid.TryParse(auctionId, out var parsedAuctionId))
            throw new ArgumentException("Invalid AuctionId format.", nameof(auctionId));

        var bids = await _bidRepository.GetAllForAuctionAsync(parsedAuctionId).ConfigureAwait(false);

        if (since is not null)
            bids = bids.Where(b => b.CreatedAtUtc >= since.Value).ToList();

        return bids
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.SourceRegionId)
            .ThenBy(b => b.Id)
            .ToList();
    }
}
