using Domain;
using Domain.Events;
using Domain.Abstractions;
using Domain.Model;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace Services;

/// <summary>
/// Core service:
/// - PlaceBid: one transaction => insert Bid, CAS update Auction, append EventStore, enqueue Outbox
/// - GetAuction: not implemented here (would switch between write/replica by consistency)
/// - ReconcileAuction: read EventStore since checkpoint, rebuild winner deterministically, write checkpoint
/// </summary>
public sealed class AuctionService : IAuctionService
{
    private readonly Region _localRegion;
    private readonly IAuctionRepository _auctionRepo;
    private readonly IBidRepository _bidRepo;
    private readonly IBidOrderingService _ordering;
    private readonly IEventStoreRepository _store;
    private readonly IEventOutboxRepository _outbox;
    private readonly IReconciliationCheckpointRepository _cp;
    private readonly IAuctionReadReplica _auctionReadReplica;
    private readonly IVehicleRepository _vehicleRepo;

    public AuctionService(
        string localRegion,
        IAuctionRepository auctionRepo,
        IBidRepository bidRepo,
        IBidOrderingService ordering,
        IEventStoreRepository store,
        IEventOutboxRepository outbox,
        IReconciliationCheckpointRepository cp,
        IAuctionReadReplica auctionReadReplica,
        IVehicleRepository vehicleRepo)
    {
        _localRegion = Enum.Parse<Region>(localRegion);
        _auctionRepo = auctionRepo;
        _bidRepo = bidRepo;
        _ordering = ordering;
        _store = store;
        _outbox = outbox;
        _cp = cp;
        _auctionReadReplica = auctionReadReplica;
        _vehicleRepo = vehicleRepo;
    }

    public async Task<Auction> CreateAuctionAsync(CreateAuctionRequest request)
    {
        var now = DateTime.UtcNow;

        var vehicle = await _vehicleRepo.GetAsync(request.VehicleId)
             ?? throw new InvalidOperationException("Vehicle not found");
        if (!string.Equals(vehicle.RegionId, _localRegion.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Auction owner region must match vehicle region.");

        var vehSnap = new VehicleSnapshot(vehicle.VehicleType, vehicle.Make, vehicle.Model, vehicle.Year);

        var newAuction = new Auction
        {
            Id = Guid.NewGuid(),
            OwnerRegionId = _localRegion,
            State = AuctionState.Draft,
            EndsAtUtc = request.EndsAtUtc,
            CurrentHighBid = null,
            CurrentSeq = 0,
            RowVersion = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _auctionRepo.InsertAsync(newAuction);

        var evId = Guid.NewGuid();
        var payload = new AuctionCreatedPayload(newAuction.Id, newAuction.OwnerRegionId.ToString(), newAuction.EndsAtUtc, vehSnap, now);
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var envelope = new EventEnvelope(evId, _localRegion.ToString(), "AuctionCreated", newAuction.Id, json, CreatedAtUtc: now);

        await _store.AppendAsync(envelope);
        await _outbox.EnqueueAsync(Guid.NewGuid(), evId, newAuction.Id, "Auction", envelope.EventType, json, now);

        return newAuction;
    }

    public async Task<BidResult> PlaceBidAsync(string auctionIdStr, BidRequest request)
    {
        var now = DateTime.UtcNow;
        if (!Guid.TryParse(auctionIdStr, out var auctionId))
            return new BidResult(false, null, "Invalid auction id");

        var a = await _auctionRepo.GetAsync(auctionId, forUpdate: true);
        if (a is null) return new BidResult(false, null, "Auction not found");
        if (a.State is not (AuctionState.Active or AuctionState.Ending)) return new BidResult(false, null, "Auction not accepting bids");
        if (now > a.EndsAtUtc) return new BidResult(false, null, "Auction already ended");

        // 1) Get next local sequence (producer-side monotonic)
        var nextSeq = await _ordering.GetNextBidSequenceAsync(auctionIdStr);

        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            Amount = request.Amount,
            Sequence = nextSeq,
            SourceRegionId = _localRegion,
            CreatedAtUtc = now,
            PartitionFlag = false, // RegionCoordinator may set this if partitioned
            UpdatedAtUtc = now
        };

        // 2) Insert bid (ensure uniqueness per (AuctionId, SourceRegionId, Sequence))
        if (await _bidRepo.ExistsAsync(auctionId, bid.SourceRegionId, bid.Sequence))
            return new BidResult(false, null, "Duplicate sequence in source region");

        await _bidRepo.InsertAsync(bid);

        // 3) CAS update auction amounts
        var expected = a.RowVersion;
        var newHigh = Math.Max(a.CurrentHighBid ?? 0m, bid.Amount);
        var ok = await _auctionRepo.TryUpdateAmountsAsync(auctionId, newHigh, nextSeq, expected);
        if (!ok) return new BidResult(false, null, "Concurrency conflict");

        // 4) Build event and persist in EventStore + Outbox
        var evId = Guid.NewGuid();
        var payload = new BidPlacedPayload(bid.Id, bid.AuctionId, bid.Amount, bid.Sequence, bid.SourceRegionId.ToString(), bid.CreatedAtUtc, bid.PartitionFlag);
        var json = JsonSerializer.Serialize(payload);

        var envelope = new EventEnvelope(
            evId,
            _localRegion.ToString(),
            "BidPlaced",
            auctionId,
            json,
            CreatedAtUtc: now);

        await _store.AppendAsync(envelope);
        await _outbox.EnqueueAsync(
            outboxId: Guid.NewGuid(),
            eventId: evId,
            auctionId: auctionId,
            aggregateType: "Auction",
            eventType: envelope.EventType,
            payloadJson: json,
            createdAtUtc: now);

        return new BidResult(true, nextSeq, "Accepted");
    }

    public async Task<Auction> GetAuctionAsync(string auctionId, ConsistencyLevel consistency)
    {
        if (!Guid.TryParse(auctionId, out Guid parsedId))
            throw new ArgumentException("Invalid auction id.");

        return consistency switch
        {
            ConsistencyLevel.Strong => await _auctionRepo.GetAsync(parsedId)
                                       ?? throw new InvalidOperationException("Auction not found"),
            ConsistencyLevel.Eventual => await _auctionReadReplica.GetFromReplicaAsync(parsedId)
                                         ?? throw new InvalidOperationException("Auction not found (replica)"),
            _ => throw new ArgumentOutOfRangeException(nameof(consistency))
        };
    }

    public async Task<ReconciliationResult> ReconcileAuctionAsync(string auctionIdStr)
    {
        if (!Guid.TryParse(auctionIdStr, out var auctionId))
            throw new ArgumentException("invalid id");

        var a = await _auctionRepo.GetAsync(auctionId);
        if (a is null) throw new InvalidOperationException("Auction not found");

        // Determine starting point from checkpoint
        var cp = await _cp.GetAsync(auctionId);
        var since = await _store.ResolveCreatedAtAsync(cp?.LastEventId);

        // Read events since checkpoint (ordered)
        var events = await _store.QuerySinceAsync(auctionId, since);

        // Apply only events that affect winner/high bid (BidPlaced, etc.)
        var knownBids = await _bidRepo.GetAllForAuctionAsync(auctionId);
        // In a richer impl you could rebuild from scratch. Here we reuse current bids.

        var winner = ConflictResolver.DecideWinner(a, knownBids);
        await _auctionRepo.SaveWinnerAsync(auctionId, winner);

        // Advance checkpoint to the last processed event (if any)
        var last = events.LastOrDefault();
        await _cp.UpsertAsync(auctionId, last?.EventId, DateTime.UtcNow);

        return new ReconciliationResult(auctionId, winner);
    }

    public async Task ActivateAsync(Guid auctionId)
    {
        var now = DateTime.UtcNow;
        var a = await _auctionRepo.GetAsync(auctionId, forUpdate: true)
                ?? throw new InvalidOperationException("Auction not found");
        if (a.State is AuctionState.Ended or AuctionState.Cancelled)
            throw new InvalidOperationException("Cannot activate a finished auction");

        // transition
        a.State = AuctionState.Active;
        a.UpdatedAtUtc = now;
        // RowVersion bump será feito pelo repo se precisares; no in-memory é suficiente setar.
        await _auctionRepo.InsertAsync(a); // in-memory “upsert”; num repo real seria Update

        // emit AuctionActivated
        var evId = Guid.NewGuid();
        var payload = new AuctionActivatedPayload(a.Id, a.OwnerRegionId.ToString(), a.EndsAtUtc, now);
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var envelope = new EventEnvelope(evId, _localRegion.ToString(), "AuctionActivated", a.Id, json, CreatedAtUtc: now);

        await _store.AppendAsync(envelope);
        await _outbox.EnqueueAsync(Guid.NewGuid(), evId, a.Id, "Auction", envelope.EventType, json, now);
    }
}
