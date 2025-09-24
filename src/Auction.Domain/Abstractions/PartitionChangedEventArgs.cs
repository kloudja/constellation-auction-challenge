using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Abstractions
{
    /// <summary>
    /// Describes current cross-region connectivity and reachability.
    /// </summary>
    public sealed record PartitionStatus(
        bool IsPartitioned,
        DateTime? SinceUtc,
        IReadOnlyDictionary<string, bool> RegionReachability);

    public sealed class PartitionChangedEventArgs : EventArgs
    {
        public string FromState { get; }
        public string ToState { get; }
        public DateTime AtUtc { get; }

        public PartitionChangedEventArgs(string fromState, string toState, DateTime atUtc)
        {
            FromState = fromState;
            ToState = toState;
            AtUtc = atUtc;
        }
    }
}
