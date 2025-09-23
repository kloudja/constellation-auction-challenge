using Domain.Events;
using Domain.Abstractions;
using System;
using System.Collections.Generic;

namespace Eventing;

/// <summary>
/// Simple in-memory pub/sub bus for a single region.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly List<Action<EventEnvelope>> _subs = new();

    public void Publish(EventEnvelope envelope)
    {
        // Fire all subscribers (best effort)
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

    private sealed class Subscription : IDisposable
    {
        private readonly List<Action<EventEnvelope>> _subs;
        private readonly Action<EventEnvelope> _handler;
        public Subscription(List<Action<EventEnvelope>> subs, Action<EventEnvelope> handler) { _subs = subs; _handler = handler; }
        public void Dispose() { _subs.Remove(_handler); }
    }
}
