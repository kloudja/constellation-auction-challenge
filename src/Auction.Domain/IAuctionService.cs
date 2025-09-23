using System.Threading.Tasks;

namespace Auction.Domain;

public interface IAuctionService
{
    Task<Auction> CreateAuctionAsync(CreateAuctionRequest request);
    Task<BidResult> PlaceBidAsync(string auctionId, BidRequest request);
    Task<Auction> GetAuctionAsync(string auctionId, ConsistencyLevel consistency);
    Task<ReconciliationResult> ReconcileAuctionAsync(string auctionId);
}
