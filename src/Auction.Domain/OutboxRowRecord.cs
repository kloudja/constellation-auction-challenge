namespace Domain;

public sealed record OutboxRowRecord(Guid Id, Guid EventId, Guid AuctionId, string AggregateType, string EventType, string PayloadJson, DateTime CreatedAtUtc, bool Published, DateTime? PublishedAtUtc);
