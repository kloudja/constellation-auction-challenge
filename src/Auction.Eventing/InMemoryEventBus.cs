using Domain.Events;

namespace Eventing;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly List<Action<EventEnvelope>> _subs = new();

    public void Publish(EventEnvelope envelope)
    {
        foreach (var h in _subs.ToArray())
        {
            try { h(envelope); } catch { }
        }
    }

    public IDisposable Subscribe(Action<EventEnvelope> handler)
    {
        _subs.Add(handler);
        return new Subscription(_subs, handler);
    }

    private sealed class Subscription(List<Action<EventEnvelope>> subs, Action<EventEnvelope> handler) : IDisposable
    {
        private readonly List<Action<EventEnvelope>> _subs = subs;
        private readonly Action<EventEnvelope> _handler = handler;

        public void Dispose() { _subs.Remove(_handler); }
    }
}
