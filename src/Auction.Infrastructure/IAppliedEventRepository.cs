namespace Infrastructure;

public interface IAppliedEventRepository
{
    Task<bool> IsAppliedAsync(Guid eventId, CancellationToken ct = default);
    Task MarkAppliedAsync(Guid eventId, DateTime appliedAtUtc, CancellationToken ct = default);
}
