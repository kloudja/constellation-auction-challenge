using System;

namespace Auction.Domain;

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
