using System;

namespace Sync;

/// <summary>
/// Connectivity monitor that exposes current link state and notifies listeners.
/// In tests you will toggle SetPartitioned/SetConnected to simulate outages.
/// </summary>
public sealed class RegionCoordinator
{
    public event EventHandler? PartitionDetected;
    public event EventHandler? PartitionHealed;

    private LinkState _state = LinkState.Connected;
    public LinkState State => _state;

    public void SetPartitioned()
    {
        if (_state == LinkState.Partitioned) return;
        _state = LinkState.Partitioned;
        PartitionDetected?.Invoke(this, EventArgs.Empty);
    }

    public void SetConnected()
    {
        var was = _state;
        _state = LinkState.Connected;
        if (was == LinkState.Partitioned) PartitionHealed?.Invoke(this, EventArgs.Empty);
    }
}
