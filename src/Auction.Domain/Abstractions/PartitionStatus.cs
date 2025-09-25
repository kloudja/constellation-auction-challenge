namespace Domain.Abstractions;

public sealed record PartitionStatus(
    bool IsPartitioned,
    DateTime? SinceUtc,
    IReadOnlyDictionary<string, bool> RegionReachability);
