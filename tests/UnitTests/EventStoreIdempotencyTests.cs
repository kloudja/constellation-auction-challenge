using FluentAssertions;
using Xunit;
using Domain.Events;
using Infrastructure.InMemory;

namespace UnitTests;

public class EventStoreIdempotencyTests
{
    [Fact(DisplayName = "First apply inserts AppliedEvent and appends to local EventStore")]
    public async Task First_Apply_Writes_Ledger_And_Appends_Event()
    {
        var appliedEventRepository = new InMemoryAppliedEventRepository();
        var destinationEventStore = new InMemoryEventStoreRepository();
        var consumer = new ApplyOnceConsumer(appliedEventRepository, destinationEventStore);

        var auctionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = new EventEnvelope(
            EventId: eventId,
            ProducerRegionId: "US",
            EventType: "BidPlaced",
            AuctionId: auctionId,
            PayloadJson: """{"BidId":"7a3a3a3a-3a3a-4a3a-8a3a-aaaaaaaaaaaa","Seq":42,"Amount":130}""",
            CreatedAtUtc: DateTime.UtcNow);

        var applied = await consumer.ApplyAsync(envelope);

        applied.Should().BeTrue();
        (await appliedEventRepository.IsAppliedAsync(eventId)).Should().BeTrue();

        var events = await destinationEventStore.QuerySinceAsync(auctionId, sinceUtc: null);
        events.Should().HaveCount(1, "first apply appends to local event store");
        events[0].EventId.Should().Be(eventId);
        events[0].EventType.Should().Be("BidPlaced");
    }

    [Fact(DisplayName = "Redelivery is idempotent due to AppliedEvent PK")]
    public async Task Redelivery_Is_Idempotent()
    {
        var appliedEventRepository = new InMemoryAppliedEventRepository();
        var destinationEventStore = new InMemoryEventStoreRepository();
        var consumer = new ApplyOnceConsumer(appliedEventRepository, destinationEventStore);

        var auctionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = new EventEnvelope(
            EventId: eventId,
            ProducerRegionId: "US",
            EventType: "BidPlaced",
            AuctionId: auctionId,
            PayloadJson: "{}",
            CreatedAtUtc: DateTime.UtcNow);

        var first = await consumer.ApplyAsync(envelope);
        var second = await consumer.ApplyAsync(envelope);

        first.Should().BeTrue("first delivery should change state and be recorded");
        second.Should().BeFalse("redelivery must be a no-op due to AppliedEvent ledger");

        var events = await destinationEventStore.QuerySinceAsync(auctionId, sinceUtc: null);
        events.Should().HaveCount(1, "idempotent consumer must not append duplicates");
        events[0].EventId.Should().Be(eventId);
    }

    private sealed class ApplyOnceConsumer(
        InMemoryAppliedEventRepository appliedEventRepository,
        InMemoryEventStoreRepository destinationEventStore)
    {
        public async Task<bool> ApplyAsync(EventEnvelope envelope, CancellationToken ct = default)
        {
            if (await appliedEventRepository.IsAppliedAsync(envelope.EventId, ct))
                return false;

            await appliedEventRepository.MarkAppliedAsync(envelope.EventId, DateTime.UtcNow, ct);

            await destinationEventStore.AppendAsync(envelope, ct);

            return true;
        }
    }
}
