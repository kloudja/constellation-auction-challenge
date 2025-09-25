using Domain.Events;

namespace Sync;

public enum LinkState { Connected, Partitioned, Healing }

public sealed class InterRegionChannel
{
    private readonly Queue<EventEnvelope> _usToEu = new();
    private readonly Queue<EventEnvelope> _euToUs = new();

    public LinkState State { get; private set; } = LinkState.Connected;

    public void SetState(LinkState state) => State = state;

    public void Send(string fromRegion, EventEnvelope e)
    {
        if (string.Equals(fromRegion, "US", StringComparison.Ordinal))
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
        var q = string.Equals(toRegion, "EU", StringComparison.Ordinal) ? _usToEu : _euToUs;
        while (q.Count > 0) yield return q.Dequeue();
    }
}
