using System;

namespace Auction.Domain;

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
