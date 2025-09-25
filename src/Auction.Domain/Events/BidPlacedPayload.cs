namespace Domain.Events;

public sealed record BidPlacedPayload(
    Guid BidId,
    Guid AuctionId,
    decimal Amount,
    long Sequence,
    string SourceRegionId,
    DateTime CreatedAtUtc,
    bool PartitionFlag);
