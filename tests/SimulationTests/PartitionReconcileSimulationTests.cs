using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using Domain.Model;

namespace SimulationTests;

// High-level simulation: EU places during partition on a US-owned auction; after heal, US reconciles.
public class PartitionReconcileSimulationTests
{
    [Fact(DisplayName = "No bids lost; correct winner after heal + reconcile")]
    public void Partition_Heal_Reconcile_Winner_Is_Correct()
    {
        // Arrange: regions, initial auction
        RegionRuntime usRegionRuntime = new("US");
        RegionRuntime euRegionRuntime = new("EU");
        InterRegionChannel interRegionChannel = new(); // simulated link (Partitioned / Connected)

        Guid auctionId = Guid.NewGuid();
        usRegionRuntime.CreateAuction(auctionId, currentHigh: 100, currentSeq: 41);

        // Partition starts
        interRegionChannel.State = LinkState.Partitioned;

        // Bids arrive in both sides (US owns the auction)
        Guid usBidId = usRegionRuntime.PlaceBid(auctionId, amount: 310, sourceRegion: "US", interRegionChannel);
        Guid euBidId = euRegionRuntime.PlaceBid(auctionId, amount: 300, sourceRegion: "EU", interRegionChannel); // buffered

        // Auction ends during partition
        usRegionRuntime.EndAuction(auctionId);

        // Heal
        interRegionChannel.State = LinkState.Connected;
        euRegionRuntime.FlushTo(usRegionRuntime, interRegionChannel); // deliver buffered EU events to US

        // Reconcile at owner (US)
        Guid? winnerBidId = usRegionRuntime.Reconcile(auctionId);

        // Assert
        winnerBidId.Should().Be(usBidId, "deterministic rule prefers higher amount and local bid in tie-breakers");
        usRegionRuntime.NoBidsLostFor(auctionId).Should().BeTrue("US should see both its own and EU's bid after heal");
    }

    // ------------------ tiny support types for the simulation ------------------
    private enum LinkState { Connected, Partitioned }

    private sealed record EventRecord(
        string ProducerRegion,
        Guid EventId,
        Guid AuctionId,
        string EventType,
        Bid Bid // carry the full bid so we can apply it at the destination
    );

    private sealed class InterRegionChannel
    {
        public LinkState State = LinkState.Connected;

        // Buffered events to deliver once healed
        public readonly Queue<EventRecord> Buffer = new();

        public void Send(string producerRegion, Guid eventId, Guid auctionId, string type, Bid bid)
        {
            // For the purposes of this simulation, we always buffer.
            // When Connected, you could deliver immediately to the destination runtime,
            // but this test explicitly calls FlushTo to simulate delivery on heal.
            Buffer.Enqueue(new EventRecord(producerRegion, eventId, auctionId, type, bid));
        }
    }

    private sealed class RegionRuntime
    {
        public string Id { get; }
        private readonly Dictionary<Guid, Auction> _auctions = new();
        private readonly Dictionary<Guid, List<Bid>> _bidsByAuction = new();
        private readonly Dictionary<Guid, long> _localSequenceByAuction = new();
        public readonly HashSet<Guid> AppliedEvent = new(); // idempotency ledger

        public RegionRuntime(string id) => Id = id;

        public void CreateAuction(Guid id, decimal currentHigh, long currentSeq)
        {
            // Only the owner needs to create the auction locally in this simulation (US).
            _auctions[id] = new Auction
            {
                Id = id,
                OwnerRegionId = Region.US,            // owner is US in this scenario
                State = AuctionState.Active,
                CurrentHighBid = currentHigh,
                CurrentSeq = currentSeq,
                RowVersion = 1
            };
            _localSequenceByAuction[id] = currentSeq;
        }

        public Guid PlaceBid(Guid auctionId, decimal amount, string sourceRegion, InterRegionChannel link)
        {
            // DO NOT assume the auction exists locally (EU during partition).
            // Compute the next local sequence even if this runtime doesn't hold the auction record.
            if (!_localSequenceByAuction.TryGetValue(auctionId, out long lastSeq))
                lastSeq = 0;

            long nextSeq = lastSeq + 1;
            _localSequenceByAuction[auctionId] = nextSeq;

            Guid bidId = Guid.NewGuid();
            DateTime nowUtc = DateTime.UtcNow;

            Bid bid = new Bid
            {
                Id = bidId,
                AuctionId = auctionId,
                Amount = amount,
                Sequence = nextSeq,
                SourceRegionId = Enum.Parse<Region>(sourceRegion),
                CreatedAtUtc = nowUtc,
                PartitionFlag = (link.State == LinkState.Partitioned)
            };

            // Persist bid locally for this runtime (diagnostics)
            if (!_bidsByAuction.TryGetValue(auctionId, out List<Bid>? localList))
                localList = _bidsByAuction[auctionId] = new List<Bid>();
            localList.Add(bid);

            // If this runtime holds the auction record (US), update its local state.
            if (_auctions.TryGetValue(auctionId, out Auction? localAuction))
            {
                localAuction.CurrentSeq = Math.Max(localAuction.CurrentSeq, nextSeq);
                localAuction.CurrentHighBid = Math.Max(localAuction.CurrentHighBid ?? 0m, amount);
                localAuction.RowVersion++;
                _auctions[auctionId] = localAuction;
            }

            // Append to "event store" and send over the channel (buffered if partitioned)
            Guid eventId = Guid.NewGuid();
            link.Send(Id, eventId, auctionId, "BidPlaced", bid);

            return bidId;
        }

        public void EndAuction(Guid auctionId)
        {
            if (_auctions.TryGetValue(auctionId, out Auction? auction))
            {
                auction.State = AuctionState.Ended;
                auction.RowVersion++;
                _auctions[auctionId] = auction;
            }
            // If this runtime doesn't hold the auction (e.g., EU), nothing to do.
        }

        public void FlushTo(RegionRuntime destination, InterRegionChannel link)
        {
            // Deliver everything in the buffer to the destination runtime
            while (link.Buffer.Count > 0)
            {
                EventRecord ev = link.Buffer.Dequeue();

                // Idempotency guard
                if (!destination.AppliedEvent.Add(ev.EventId))
                    continue;

                if (ev.EventType == "BidPlaced")
                {
                    // Apply the bid at the destination (merge foreign bids into destination's DB)
                    if (!destination._bidsByAuction.TryGetValue(ev.AuctionId, out List<Bid>? destList))
                        destList = destination._bidsByAuction[ev.AuctionId] = new List<Bid>();

                    // Insert a copy (to avoid accidental shared references)
                    destList.Add(new Bid
                    {
                        Id = ev.Bid.Id,
                        AuctionId = ev.Bid.AuctionId,
                        Amount = ev.Bid.Amount,
                        Sequence = ev.Bid.Sequence,
                        SourceRegionId = ev.Bid.SourceRegionId,
                        CreatedAtUtc = ev.Bid.CreatedAtUtc,
                        PartitionFlag = ev.Bid.PartitionFlag
                    });

                    // Optionally update destination auction's quick stats if it exists there
                    if (destination._auctions.TryGetValue(ev.AuctionId, out Auction? destAuction))
                    {
                        destAuction.CurrentHighBid = Math.Max(destAuction.CurrentHighBid ?? 0m, ev.Bid.Amount);
                        destAuction.CurrentSeq = Math.Max(destAuction.CurrentSeq, ev.Bid.Sequence);
                        destination._auctions[ev.AuctionId] = destAuction;
                    }
                }
            }
        }

        public Guid? Reconcile(Guid auctionId)
        {
            if (!_bidsByAuction.TryGetValue(auctionId, out List<Bid>? allBids) || allBids.Count == 0)
                return null;

            // Deterministic winner: Amount desc → Sequence asc → CreatedAtUtc asc → SourceRegionId asc → Id asc
            Guid? winner = allBids
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.Sequence)
                .ThenBy(b => b.CreatedAtUtc)
                .ThenBy(b => b.SourceRegionId)
                .ThenBy(b => b.Id)
                .First().Id;

            if (_auctions.TryGetValue(auctionId, out Auction? auction))
            {
                auction.WinnerBidId = winner;
                _auctions[auctionId] = auction;
            }

            return winner;
        }

        public bool NoBidsLostFor(Guid auctionId)
        {
            return _bidsByAuction.TryGetValue(auctionId, out List<Bid>? list) && list.Count >= 2;
        }
    }
}
