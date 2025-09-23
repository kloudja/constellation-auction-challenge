using Domain.Events;
using System;

namespace Domain.Abstractions;

public interface IEventBus
{
    void Publish(EventEnvelope envelope);
    IDisposable Subscribe(Action<EventEnvelope> handler);
}
