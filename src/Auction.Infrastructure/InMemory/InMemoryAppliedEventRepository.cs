using System.Collections.Concurrent;

namespace Infrastructure.InMemory;

public sealed class InMemoryAppliedEventRepository : IAppliedEventRepository
{
    private readonly ConcurrentDictionary<Guid, DateTime> _applied = new();
    public Task<bool> IsAppliedAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(_applied.ContainsKey(eventId));
    public Task MarkAppliedAsync(Guid eventId, DateTime appliedAtUtc, CancellationToken ct = default)
    { _applied[eventId] = appliedAtUtc; return Task.CompletedTask; }
}
