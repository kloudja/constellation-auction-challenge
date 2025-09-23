namespace Auction.Domain;

public enum ConsistencyLevel { Strong, Eventual }

public class Auction
{
    public Guid Id;
    public string OwnerRegionId = "US";
    public string State = "Draft";
    public decimal? CurrentHighBid;
    public long CurrentSeq;
    public long RowVersion;
    public Guid? WinnerBidId;
}

public class Bid
{
    public Guid Id;
    public Guid AuctionId;
    public decimal Amount;
    public long Sequence;
    public string SourceRegionId = "US";
    public DateTime CreatedAtUtc;
    public bool PartitionFlag;
}

public record CreateAuctionRequest(Guid VehicleId, DateTime EndsAtUtc);
public record BidRequest(decimal Amount, string SourceRegionId);
public record BidResult(bool Accepted, long? Sequence, string Reason);
public record ReconciliationResult(Guid AuctionId, Guid? WinnerBidId);

public interface IAuctionService
{
    Task<Auction> CreateAuctionAsync(CreateAuctionRequest request);
    Task<BidResult> PlaceBidAsync(string auctionId, BidRequest request);
    Task<Auction> GetAuctionAsync(string auctionId, ConsistencyLevel consistency);
    Task<ReconciliationResult> ReconcileAuctionAsync(string auctionId);
}

public interface IRegionCoordinator
{
    Task<bool> IsRegionReachableAsync(string region);
    Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation);
    Task<object> GetPartitionStatusAsync();
    event EventHandler<EventArgs>? PartitionDetected;
    event EventHandler<EventArgs>? PartitionHealed;
}

public interface IBidOrderingService
{
    Task<long> GetNextBidSequenceAsync(string auctionId);
    Task<bool> ValidateBidOrderAsync(string auctionId, Bid bid);
    Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null);
}
