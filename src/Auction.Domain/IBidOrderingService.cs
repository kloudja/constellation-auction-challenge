using Domain.Model;

namespace Domain;

public interface IBidOrderingService
{
    Task<long> GetNextBidSequenceAsync(string auctionId);
    Task<BidAcceptance> ValidateBidOrderAsync(string auctionId, Bid bid);
    Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null);
}
