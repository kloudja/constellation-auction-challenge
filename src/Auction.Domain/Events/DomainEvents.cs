using System;

namespace Domain.Events;

/// <summary>Base envelope for event bus.</summary>
public sealed record EventEnvelope(
    Guid EventId,
    string ProducerRegionId,
    string EventType,
    Guid AuctionId,
    string PayloadJson,
    DateTime CreatedAtUtc);

/// <summary>Payload for BidPlaced (embedded inside EventEnvelope.PayloadJson).</summary>
public sealed record BidPlacedPayload(
    Guid BidId,
    Guid AuctionId,
    decimal Amount,
    long Sequence,
    string SourceRegionId,
    DateTime CreatedAtUtc,
    bool PartitionFlag);

public sealed record VehicleSnapshot(string VehicleType, string Make, string Model, int Year);

public sealed record AuctionCreatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    VehicleSnapshot Vehicle,
    DateTime CreatedAtUtc);

public sealed record AuctionActivatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    VehicleSnapshot Vehicle,
    DateTime CreatedAtUtc);