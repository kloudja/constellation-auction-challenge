namespace Domain.Events;

public sealed record AuctionCreatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    VehicleSnapshot Vehicle,
    DateTime CreatedAtUtc);
