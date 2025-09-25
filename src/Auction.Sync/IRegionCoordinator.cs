using Domain.Abstractions;

namespace Sync;

public interface IRegionCoordinator
{
    Task<bool> IsRegionReachableAsync(string region, CancellationToken ct = default);
    Task<PartitionStatus> GetPartitionStatusAsync(CancellationToken ct = default);
    Task<T> ExecuteInRegionAsync<T>(string region, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    event EventHandler<PartitionChangedEventArgs>? PartitionDetected;
    event EventHandler<PartitionChangedEventArgs>? PartitionHealed;
}
