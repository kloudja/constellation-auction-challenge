using System;

namespace Domain.Model;


public sealed class Auction
{
    public Guid Id { get; init; }
    public Region OwnerRegionId { get; set; } = Region.US;
    public AuctionState State { get; set; } = AuctionState.Draft;
    public DateTime EndsAtUtc { get; set; }

    public decimal? CurrentHighBid { get; set; }
    public long CurrentSeq { get; set; }
    public long RowVersion { get; set; }

    public Guid? WinnerBidId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
