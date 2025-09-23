using Domain;
using Domain.Events;
using Domain.Abstractions;
using Domain.Events;
using Domain.Model;
using Sync;
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
        var toRegion = _localRegion; // "EU" drains US->EU, "US" drains EU->US
        var cnt = 0;
        foreach (var e in _link.DrainTo(toRegion))
        {
            if (await _applied.IsAppliedAsync(e.EventId, ct)) continue;

            switch (e.EventType)
            {
                case "BidPlaced":
                    var bp = JsonSerializer.Deserialize<BidPlacedPayload>(e.PayloadJson)!;

                    if (!await _bids.ExistsAsync(bp.AuctionId, Enum.Parse<Region>(bp.SourceRegionId), bp.Sequence))
                    {
                        var bid = new Bid
                        {
                            Id = bp.BidId,
                            AuctionId = bp.AuctionId,
                            Amount = bp.Amount,
                            Sequence = bp.Sequence,
                            SourceRegionId = Enum.Parse<Region>(bp.SourceRegionId),
                            CreatedAtUtc = bp.CreatedAtUtc,
                            PartitionFlag = bp.PartitionFlag,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        await _bids.InsertAsync(bid, ct);
                    }

                    await _applied.MarkAppliedAsync(e.EventId, DateTime.UtcNow, ct);
                    await _store.AppendAsync(e, ct);
                    cnt++;
                    break;

                default:
                    break;
            }
        }
        return cnt;
    }

    public void Dispose() => _subscription.Dispose();
}
