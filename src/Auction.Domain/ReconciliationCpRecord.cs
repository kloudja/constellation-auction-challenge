namespace Domain;

public sealed record ReconciliationCpRecord(Guid AuctionId, Guid? LastEventId, DateTime? LastRunAtUtc);
