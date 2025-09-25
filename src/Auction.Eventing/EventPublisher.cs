using Domain.Events;
using Infrastructure;

namespace Eventing;

public sealed class EventPublisher(string region, IEventOutboxRepository outbox, IEventStoreRepository store, IEventBus bus)
{
    private readonly string _region = region;
    private readonly IEventOutboxRepository _outbox = outbox;
    private readonly IEventStoreRepository _store = store;
    private readonly IEventBus _bus = bus;

    public async Task<int> PublishPendingAsync(int batchSize = 128, CancellationToken ct = default)
    {
        var batch = await _outbox.DequeuePendingAsync(batchSize, ct);
        var count = 0;

        foreach (var row in batch)
        {
            var envelope = new EventEnvelope(row.EventId, _region, row.EventType, row.AuctionId, row.PayloadJson, row.CreatedAtUtc);
            try
            {
                _bus.Publish(envelope);
                await _outbox.MarkPublishedAsync(row.Id, DateTime.UtcNow, ct);
                count++;
            }
            catch
            {
            }
        }
        return count;
    }
}
