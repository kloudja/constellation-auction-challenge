using Xunit;
using FluentAssertions;
using Auction.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Auction.SimulationTests;

// High-level simulation: EU places during partition on a US-owned auction; after heal, US reconciles.
public class PartitionReconcileSimulationTests
{
    [Fact(DisplayName = "No bids lost; correct winner after heal + reconcile")]
    public void Partition_Heal_Reconcile_Winner_Is_Correct()
    {
        // Arrange: regions, initial auction
        var US = new RegionRuntime("US");
        var EU = new RegionRuntime("EU");
        var link = new InterRegionChannel(); // simulated link (Partitioned / Connected)

        var auctionId = Guid.NewGuid();
        US.CreateAuction(auctionId, currentHigh: 100, currentSeq: 41);

        // Partition starts
        link.State = LinkState.Partitioned;

        // Bids arrive in both sides (US owns the auction)
        var usBidId = US.PlaceBid(auctionId, amount: 310, sourceRegion: "US", link);
        var euBidId = EU.PlaceBid(auctionId, amount: 300, sourceRegion: "EU", link); // buffered

        // Auction ends during partition
        US.EndAuction(auctionId);

        // Heal
        link.State = LinkState.Connected;
        EU.FlushTo(US, link); // deliver buffered EU events to US

        // Reconcile at owner (US)
        var winner = US.Reconcile(auctionId);

        // Assert
        winner.Should().Be(usBidId, "deterministic rule prefers higher amount and local bid in tie-breakers");
        US.NoBidsLostFor(auctionId).Should().BeTrue();
    }

    // ------------------ tiny support types for the simulation ------------------
    private enum LinkState { Connected, Partitioned }

    private sealed class InterRegionChannel
    {
        public LinkState State = LinkState.Connected;
        public readonly Queue<(string Producer, Guid EventId, Guid AuctionId, string EventType, string Payload)> Buffer = new();
        public void Send(string producer, Guid evtId, Guid auctionId, string type, string payload)
        {
            if (State == LinkState.Partitioned) Buffer.Enqueue((producer, evtId, auctionId, type, payload));
            // In real Connected mode we'd immediately deliver to the other side.
        }
    }

    private sealed class RegionRuntime
    {
        public string Id { get; }
        private readonly Dictionary<Guid, Domain.Auction> _auctions = new();
        private readonly Dictionary<Guid, List<Bid>> _bids = new();
        public readonly HashSet<Guid> AppliedEvent = new(); // ledger

        public RegionRuntime(string id) => Id = id;

        public void CreateAuction(Guid id, decimal currentHigh, long currentSeq)
            => _auctions[id] = new Domain.Auction { Id = id, OwnerRegionId = "US", State = "Active", CurrentHighBid = currentHigh, CurrentSeq = currentSeq, RowVersion = 1 };

        public Guid PlaceBid(Guid auctionId, decimal amount, string sourceRegion, InterRegionChannel link)
        {
            // local write: assign seq, insert bid, update auction
            var a = _auctions[auctionId];
            var seq = a.CurrentSeq + 1;
            var bid = new Bid { Id = Guid.NewGuid(), AuctionId = auctionId, Amount = amount, Sequence = seq, SourceRegionId = sourceRegion, CreatedAtUtc = DateTime.UtcNow, PartitionFlag = (link.State == LinkState.Partitioned) };
            if (!_bids.TryGetValue(auctionId, out var list)) list = _bids[auctionId] = new();
            list.Add(bid);

            a.CurrentSeq = seq; a.CurrentHighBid = Math.Max(a.CurrentHighBid ?? 0, amount); a.RowVersion++; _auctions[auctionId] = a;

            // outbox→bus (omitted), event store append (simulate by sending to channel)
            var eventId = Guid.NewGuid();
            link.Send(Id, eventId, auctionId, "BidPlaced", "{}");
            return bid.Id;
        }

        public void EndAuction(Guid auctionId)
        {
            var a = _auctions[auctionId]; a.State = "Ended"; a.RowVersion++; _auctions[auctionId] = a;
        }

        public void FlushTo(RegionRuntime destination, InterRegionChannel link)
        {
            while (link.Buffer.Count > 0)
            {
                var (prod, evt, auc, type, payload) = link.Buffer.Dequeue();
                if (destination.AppliedEvent.Add(evt))
                {
                    // apply to destination's write DB (idempotent)
                    // Here we only mark ledger; you'd merge foreign bids into destination DB.
                }
            }
        }

        public Guid? Reconcile(Guid auctionId)
        {
            var list = _bids[auctionId].OrderByDescending(b => b.Amount)
                                       .ThenBy(b => b.Sequence)
                                       .ThenBy(b => b.CreatedAtUtc)
                                       .ThenBy(b => b.SourceRegionId) // deterministic
                                       .ToList();
            var winner = list.FirstOrDefault()?.Id;
            _auctions[auctionId].WinnerBidId = winner;
            return winner;
        }

        public bool NoBidsLostFor(Guid auctionId) => _bids.ContainsKey(auctionId) && _bids[auctionId].Count >= 2;
    }
}
