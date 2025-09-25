using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System;
using System.Linq;

namespace UnitTests;

// Simulates the outbox polling/publish pattern. Publisher marks row Published=true after emit.
// Required by "Simple event bus + at-least-once + event storage / replay" (spec).
public class OutboxPublisherTests
{
    [Fact(DisplayName = "Publisher publishes pending outbox and marks Published=true")]
    public void Publishes_And_Marks_Published()
    {
        var outbox = new InMemoryOutbox();
        var bus    = new InMemoryBus();
        var pub    = new Publisher(outbox, bus);

        var outboxId = Guid.NewGuid();
        outbox.Enqueue(new OutboxRow(outboxId, "Auction", Guid.NewGuid(), "BidPlaced", "{}",
                          CreatedAtUtc: DateTime.UtcNow, Published: false));

        pub.PublishPending();

        bus.Published.Count.Should().Be(1);
        outbox.Get(outboxId)!.Published.Should().BeTrue();
        outbox.Get(outboxId)!.PublishedAtUtc.Should().NotBeNull();
    }

    // ---- tiny in-memory stubs for the test ----
    private sealed record OutboxRow(Guid Id, string AggregateType, Guid AuctionId, string EventType, string PayloadJson, DateTime CreatedAtUtc, bool Published, DateTime? PublishedAtUtc = null);

    private sealed class InMemoryOutbox
    {
        private readonly Dictionary<Guid,OutboxRow> _rows = new();
        public void Enqueue(OutboxRow r) => _rows[r.Id] = r;
        public IEnumerable<OutboxRow> Pending() => _rows.Values.Where(x => !x.Published).OrderBy(x => x.CreatedAtUtc);
        public OutboxRow? Get(Guid id) => _rows.TryGetValue(id, out var r) ? r : null;
        public void MarkPublished(Guid id, DateTime at) => _rows[id] = _rows[id] with { Published = true, PublishedAtUtc = at };
    }

    private sealed class InMemoryBus { public List<(string,string)> Published { get; } = new(); public void Publish(string type, string payload) => Published.Add((type,payload)); }

    private sealed class Publisher
    {
        private readonly InMemoryOutbox _outbox; private readonly InMemoryBus _bus;
        public Publisher(InMemoryOutbox outbox, InMemoryBus bus) { _outbox = outbox; _bus = bus; }
        public void PublishPending()
        {
            foreach (var row in _outbox.Pending())
            {
                _bus.Publish(row.EventType, row.PayloadJson);
                _outbox.MarkPublished(row.Id, DateTime.UtcNow); // publish-after-commit marking
            }
        }
    }
}
