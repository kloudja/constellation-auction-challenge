using System;

namespace Domain.Model;

public sealed class Bid
{
    public Guid Id { get; init; }
    public Guid AuctionId { get; init; }
    public decimal Amount { get; init; }

    public long Sequence { get; init; }
    public Region SourceRegionId { get; init; } = Region.US;
    public DateTime CreatedAtUtc { get; init; }
    public bool PartitionFlag { get; init; }

    public string? BidderId { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? DeletedAtUtc { get; init; }
}
