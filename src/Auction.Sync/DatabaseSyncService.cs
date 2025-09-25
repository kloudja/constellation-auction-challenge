using Domain.Events;
using Domain.Model;
using System.Text.Json;
using Eventing;
using Infrastructure;

namespace Sync;

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

        _subscription = _localBus.Subscribe(envelope =>
        {
            _link.Send(_localRegion, envelope);
        });
    }

    public async Task<int> DrainAndApplyAsync(CancellationToken ct = default)
    {
        var toRegion = _localRegion /*== Region.EU.ToString() ? Region.US.ToString() : Region.EU.ToString()*/; // "EU" drains US->EU, "US" drains EU->US
        var eventsApplied = 0;
        foreach (var e in _link.DrainTo(toRegion))
        {
            if (await _applied.IsAppliedAsync(e.EventId, ct).ConfigureAwait(false)) continue;

            switch (e.EventType)
            {
                case "AuctionCreated":
                    {
                        var auctionCreatedPayload = JsonSerializer.Deserialize<AuctionCreatedPayload>(e.PayloadJson)!;
                        var existing = await _auctions.GetAsync(auctionCreatedPayload.AuctionId).ConfigureAwait(false);
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
                            await _auctions.InsertAsync(mirror, ct).ConfigureAwait(false);
                        }
                        await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct).ConfigureAwait(false);
                        await _store.AppendAsync(e, ct).ConfigureAwait(false);
                        eventsApplied++;
                        break;
                    }
                case "AuctionActivated":
                    {
                        var auctionActivatedPayload = JsonSerializer.Deserialize<AuctionActivatedPayload>(e.PayloadJson)!;

                        var existingAuction = await _auctions.GetAsync(auctionActivatedPayload.AuctionId).ConfigureAwait(false) ?? new Auction
                        {
                            Id = auctionActivatedPayload.AuctionId,
                            OwnerRegionId = Enum.Parse<Region>(auctionActivatedPayload.OwnerRegionId),
                            EndsAtUtc = auctionActivatedPayload.EndsAtUtc,
                            CreatedAtUtc = auctionActivatedPayload.CreatedAtUtc
                        };
                        existingAuction.State = AuctionState.Active;
                        existingAuction.UpdatedAtUtc = DateTime.UtcNow;
                        await _auctions.InsertAsync(existingAuction, ct).ConfigureAwait(false);

                        await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct).ConfigureAwait(false);
                        await _store.AppendAsync(e, ct).ConfigureAwait(false);
                        eventsApplied++;
                        break;
                    }
                case "BidPlaced":
                    var bidPlacedPayload = JsonSerializer.Deserialize<BidPlacedPayload>(e.PayloadJson)!;

                    if (!await _bids.ExistsAsync(bidPlacedPayload.AuctionId, Enum.Parse<Region>(bidPlacedPayload.SourceRegionId), bidPlacedPayload.Sequence).ConfigureAwait(false))
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
                        await _bids.InsertAsync(bid, ct).ConfigureAwait(false);
                    }

                    await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct).ConfigureAwait(false);
                    await _store.AppendAsync(e, ct).ConfigureAwait(false);
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
