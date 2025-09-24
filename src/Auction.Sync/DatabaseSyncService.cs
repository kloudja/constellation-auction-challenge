using Domain.Events;
using Domain.Abstractions;
using Domain.Model;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sync;

/// <summary>
/// Two roles per region:
/// 1) Subscribe to local bus and forward envelopes to the inter-region channel (producer role).
/// 2) Drain from the channel to THIS region, apply idempotently, and append to local EventStore (consumer role).
/// </summary>
public sealed class DatabaseSyncService : IDisposable
{
    private readonly string _localRegion;
    private readonly IEventBus _localBus;
    private readonly InterRegionChannel _link;

    private readonly IAppliedEventRepository _applied;
    private readonly IBidRepository _bids;
    private readonly IAuctionRepository _auctions;
    private readonly IEventStoreRepository _store;

    private readonly IDisposable _subscription;

    public DatabaseSyncService(
        string localRegion,
        IEventBus localBus,
        InterRegionChannel link,
        IAppliedEventRepository applied,
        IBidRepository bids,
        IAuctionRepository auctions,
        IEventStoreRepository store)
    {
        _localRegion = localRegion;
        _localBus = localBus;
        _link = link;
        _applied = applied;
        _bids = bids;
        _auctions = auctions;
        _store = store;

        // 1) Producer role: on local publish, forward to the other region via channel
        _subscription = _localBus.Subscribe(envelope =>
        {
            // Forward every event to the other region (owner will reconcile)
            _link.Send(_localRegion, envelope);
        });
    }

    /// <summary>
    /// 2) Consumer role: drain channel events destined to THIS region and apply idempotently.
    /// Call this after heal or periodically.
    /// </summary>
    public async Task<int> DrainAndApplyAsync(CancellationToken ct = default)
    {
        var toRegion = _localRegion /*== Region.EU.ToString() ? Region.US.ToString() : Region.EU.ToString()*/; // "EU" drains US->EU, "US" drains EU->US
        var eventsApplied = 0;
        foreach (var e in _link.DrainTo(toRegion))
        {
            if (await _applied.IsAppliedAsync(e.EventId, ct)) continue;

            switch (e.EventType)
            {
                case "AuctionCreated":
                    {
                        var auctionCreatedPayload = JsonSerializer.Deserialize<AuctionCreatedPayload>(e.PayloadJson)!;
                        var existing = await _auctions.GetAsync(auctionCreatedPayload.AuctionId);
                        if (existing is null)
                        {
                            var mirror = new Auction
                            {
                                Id = auctionCreatedPayload.AuctionId,
                                OwnerRegionId = Enum.Parse<Region>(auctionCreatedPayload.OwnerRegionId),
                                State = AuctionState.Draft,
                                EndsAtUtc = auctionCreatedPayload.EndsAtUtc,
                                CurrentHighBid = null,
                                CurrentSeq = 0,
                                RowVersion = 0,
                                CreatedAtUtc = auctionCreatedPayload.CreatedAtUtc,
                                UpdatedAtUtc = DateTime.UtcNow,
                            };
                            await _auctions.InsertAsync(mirror, ct);
                        }
                        await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct);
                        await _store.AppendAsync(e, ct);
                        eventsApplied++;
                        break;
                    }
                case "AuctionActivated":
                    {
                        var auctionActivatedPayload = JsonSerializer.Deserialize<AuctionActivatedPayload>(e.PayloadJson)!;

                        var existingAuction = await _auctions.GetAsync(auctionActivatedPayload.AuctionId) ?? new Auction
                        {
                            Id = auctionActivatedPayload.AuctionId,
                            OwnerRegionId = Enum.Parse<Region>(auctionActivatedPayload.OwnerRegionId),
                            EndsAtUtc = auctionActivatedPayload.EndsAtUtc,
                            CreatedAtUtc = auctionActivatedPayload.CreatedAtUtc
                        };
                        existingAuction.State = AuctionState.Active;
                        existingAuction.UpdatedAtUtc = DateTime.UtcNow;
                        await _auctions.InsertAsync(existingAuction, ct); 

                        await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct);
                        await _store.AppendAsync(e, ct);
                        eventsApplied++;
                        break;
                    }
                case "BidPlaced":
                    var bidPlacedPayload = JsonSerializer.Deserialize<BidPlacedPayload>(e.PayloadJson)!;

                    if (!await _bids.ExistsAsync(bidPlacedPayload.AuctionId, Enum.Parse<Region>(bidPlacedPayload.SourceRegionId), bidPlacedPayload.Sequence))
                    {
                        var bid = new Bid
                        {
                            Id = bidPlacedPayload.BidId,
                            AuctionId = bidPlacedPayload.AuctionId,
                            Amount = bidPlacedPayload.Amount,
                            Sequence = bidPlacedPayload.Sequence,
                            SourceRegionId = Enum.Parse<Region>(bidPlacedPayload.SourceRegionId),
                            CreatedAtUtc = bidPlacedPayload.CreatedAtUtc,
                            PartitionFlag = bidPlacedPayload.PartitionFlag,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        await _bids.InsertAsync(bid, ct);
                    }

                    await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct);
                    await _store.AppendAsync(e, ct);
                    eventsApplied++;
                    break;

                default:
                    break;
            }
        }
        return eventsApplied;
    }

    public void Dispose() => _subscription.Dispose();
}
