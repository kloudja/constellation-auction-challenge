using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Abstractions
{
    public interface IRegionCoordinator
    {
        Task<bool> IsRegionReachableAsync(string region, CancellationToken ct = default);
        Task<PartitionStatus> GetPartitionStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// Execute an operation as if it targeted the specified region.
        /// Implementations may short-circuit or throw when region is unreachable/partitioned.
        /// </summary>
        Task<T> ExecuteInRegionAsync<T>(string region, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

        event EventHandler<PartitionChangedEventArgs>? PartitionDetected;
        event EventHandler<PartitionChangedEventArgs>? PartitionHealed;
    }
}
