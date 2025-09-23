using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Auction.Domain;

public interface IBidOrderingService
{
    Task<long> GetNextBidSequenceAsync(string auctionId);
    Task<bool> ValidateBidOrderAsync(string auctionId, Bid bid);
    Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null);
}
