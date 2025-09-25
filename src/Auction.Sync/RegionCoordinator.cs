using Domain.Abstractions;

namespace Sync;

public sealed class RegionCoordinator : IRegionCoordinator
{
    private readonly HashSet<string> _knownRegions = new(StringComparer.OrdinalIgnoreCase) { "US", "EU" };
    private LinkState _state = LinkState.Connected;
    private DateTime? _sinceUtc = null;
    private readonly Dictionary<string, bool> _reachability = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = true,
        ["EU"] = true
    };

    public event EventHandler<PartitionChangedEventArgs>? PartitionDetected;
    public event EventHandler<PartitionChangedEventArgs>? PartitionHealed;

    public void SetPartitioned()
    {
        if (_state == LinkState.Partitioned) return;
        var from = _state.ToString();
        _state = LinkState.Partitioned;
        _sinceUtc = DateTime.UtcNow;

        foreach (var region in _knownRegions) _reachability[region] = false;
        PartitionDetected?.Invoke(this, new PartitionChangedEventArgs(from, _state.ToString(), _sinceUtc.Value));
    }

    public void SetConnected()
    {
        if (_state == LinkState.Connected) return;
        var from = _state.ToString();
        _state = LinkState.Connected;
        var at = DateTime.UtcNow;

        foreach (var region in _knownRegions) _reachability[region] = true;
        PartitionHealed?.Invoke(this, new PartitionChangedEventArgs(from, _state.ToString(), at));
        _sinceUtc = null;
    }

    public Task<bool> IsRegionReachableAsync(string region, CancellationToken ct = default)
    {
        if (!_knownRegions.Contains(region)) return Task.FromResult(false);
        return Task.FromResult(_reachability.TryGetValue(region, out var reachable) && reachable);
    }

    public Task<PartitionStatus> GetPartitionStatusAsync(CancellationToken ct = default)
    {
        PartitionStatus status = new(
            IsPartitioned: _state == LinkState.Partitioned,
            SinceUtc: _sinceUtc,
            RegionReachability: new Dictionary<string, bool>(_reachability, StringComparer.Ordinal));
        return Task.FromResult(status);
    }

    public Task<T> ExecuteInRegionAsync<T>(string region, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        if (!_knownRegions.Contains(region))
            throw new ArgumentException($"Unknown region '{region}'.");

        if (_state == LinkState.Partitioned && !_reachability[region])
            throw new InvalidOperationException($"Region '{region}' is unreachable due to partition.");

        return operation(ct);
    }
}
