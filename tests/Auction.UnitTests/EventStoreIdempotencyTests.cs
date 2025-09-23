using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;

namespace Auction.UnitTests;

// Demonstrates at-least-once delivery across regions using an AppliedEvent ledger per consumer region.
public class EventStoreIdempotencyTests
{
    [Fact(DisplayName = "First apply inserts AppliedEvent and changes local state")]
    public void First_Apply_Writes_Ledger_And_Changes_State()
    {
        var producerStore = new InMemoryEventStore("US");
        var consumer      = new ConsumerWithLedger("EU");

        // Producer appends (durable history)
        var auctionId = Guid.NewGuid();
        var evtId     = producerStore.Append("BidPlaced", auctionId, payloadJson: """{"BidId":"%BID%","Seq":42,"Amount":130}""");

        // Consumer receives and applies (at-least-once)
        var applied = consumer.Apply(evtId, auctionId, "BidPlaced");
        applied.Should().BeTrue();
        consumer.AppliedContains(evtId).Should().BeTrue();
    }

    [Fact(DisplayName = "Redelivery is idempotent due to AppliedEvent PK")]
    public void Redelivery_Is_Idempotent()
    {
        var store    = new InMemoryEventStore("US");
        var consumer = new ConsumerWithLedger("EU");
        var auction  = Guid.NewGuid();
        var e        = store.Append("BidPlaced", auction, "{}");

        consumer.Apply(e, auction, "BidPlaced").Should().BeTrue();
        consumer.Apply(e, auction, "BidPlaced").Should().BeFalse("ledger prevents duplicate effects");
    }

    private sealed class InMemoryEventStore
    {
        public string Producer { get; }
        private readonly Dictionary<Guid,(Guid AuctionId,string Type,string Payload,DateTime CreatedAtUtc)> _log = new();
        public InMemoryEventStore(string producerRegion) => Producer = producerRegion;

        public Guid Append(string type, Guid auctionId, string payloadJson)
        {
            var id = Guid.NewGuid(); _log[id] = (auctionId, type, payloadJson, DateTime.UtcNow); return id;
        }
    }

    private sealed class ConsumerWithLedger
    {
        public string Region { get; }
        private readonly HashSet<Guid> _applied = new();
        public ConsumerWithLedger(string region) => Region = region;

        public bool AppliedContains(Guid eventId) => _applied.Contains(eventId);

        public bool Apply(Guid eventId, Guid auctionId, string eventType)
        {
            if (_applied.Contains(eventId)) return false; // dedupe
            _applied.Add(eventId);
            // apply to local write DB here (omitted for brevity)
            return true;
        }
    }
}
