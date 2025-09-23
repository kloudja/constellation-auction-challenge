using System;
using System.Threading.Tasks;

namespace Domain;

public interface IRegionCoordinator
{
    Task<bool> IsRegionReachableAsync(string region);
    Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation);
    Task<object> GetPartitionStatusAsync();
    event EventHandler<EventArgs>? PartitionDetected;
    event EventHandler<EventArgs>? PartitionHealed;
}
