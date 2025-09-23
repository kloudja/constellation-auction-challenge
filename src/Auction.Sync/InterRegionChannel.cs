using Domain.Events;
using System.Collections.Generic;

namespace Sync;

/// <summary>
/// Simulated inter-region link with 2 mailboxes (US->EU and EU->US).
/// Supports Connected/Partitioned/Healing by buffering and flushing batches.
/// </summary>
public enum LinkState { Connected, Partitioned, Healing }

public sealed class InterRegionChannel
{
    private readonly Queue<EventEnvelope> _usToEu = new();
    private readonly Queue<EventEnvelope> _euToUs = new();

    public LinkState State { get; private set; } = LinkState.Connected;

    public void SetState(LinkState state) => State = state;

    public void Send(string fromRegion, EventEnvelope e)
    {
        if (fromRegion == "US")
        {
            if (State == LinkState.Partitioned) _usToEu.Enqueue(e);
            else _usToEu.Enqueue(e);
        }
        else
        {
            if (State == LinkState.Partitioned) _euToUs.Enqueue(e);
            else _euToUs.Enqueue(e);
        }
    }

    public IEnumerable<EventEnvelope> DrainTo(string toRegion)
    {
        var q = toRegion == "EU" ? _usToEu : _euToUs;
        while (q.Count > 0) yield return q.Dequeue();
    }
}
