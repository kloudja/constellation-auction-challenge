namespace Domain.Events;

public sealed record AuctionActivatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    DateTime CreatedAtUtc);
