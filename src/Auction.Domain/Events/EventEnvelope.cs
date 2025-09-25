namespace Domain.Events;

public sealed record EventEnvelope(
    Guid EventId,
    string ProducerRegionId,
    string EventType,
    Guid AuctionId,
    string PayloadJson,
    DateTime CreatedAtUtc);
