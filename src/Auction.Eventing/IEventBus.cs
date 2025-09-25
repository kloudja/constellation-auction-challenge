using Domain.Events;

namespace Eventing;

public interface IEventBus
{
    void Publish(EventEnvelope envelope);
    IDisposable Subscribe(Action<EventEnvelope> handler);
}
