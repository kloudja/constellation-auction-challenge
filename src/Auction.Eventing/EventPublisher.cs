using Domain.Events;
using Domain.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Eventing;

/// <summary>
/// Polls the outbox, publishes to local bus, marks rows as Published=true.
/// </summary>
public sealed class EventPublisher
{
    private readonly string _region;
    private readonly IEventOutboxRepository _outbox;
    private readonly IEventStoreRepository _store;
    private readonly IEventBus _bus;

    public EventPublisher(string region, IEventOutboxRepository outbox, IEventStoreRepository store, IEventBus bus)
    {
        _region = region;
        _outbox = outbox;
        _store = store;
        _bus = bus;
    }

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
