using Domain;
using Domain.Events;
using Domain.Model;
using System.Text.Json;
using Infrastructure;

namespace Services;

public sealed class AuctionService(
    string localRegion,
    IAuctionRepository auctionRepo,
    IBidRepository bidRepo,
    IBidOrderingService ordering,
    IEventStoreRepository store,
    IEventOutboxRepository outbox,
    IReconciliationCheckpointRepository cp,
    IAuctionReadReplica auctionReadReplica,
    IVehicleRepository vehicleRepo) : IAuctionService
{
    private readonly Region _localRegion = Enum.Parse<Region>(localRegion);
    private readonly IAuctionRepository _auctionRepo = auctionRepo;
    private readonly IBidRepository _bidRepo = bidRepo;
    private readonly IBidOrderingService _bidOrderingService = ordering;
    private readonly IEventStoreRepository _store = store;
    private readonly IEventOutboxRepository _outboxRepo = outbox;
    private readonly IReconciliationCheckpointRepository _reconciliationCheckpointRepo = cp;
    private readonly IAuctionReadReplica _auctionReadReplica = auctionReadReplica;
    private readonly IVehicleRepository _vehicleRepo = vehicleRepo;

    public async Task<Auction> CreateAuctionAsync(CreateAuctionRequest request)
    {
        var now = DateTime.UtcNow;

        var vehicle = await _vehicleRepo.GetAsync(request.VehicleId)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Vehicle not found");
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
        await _auctionRepo.InsertAsync(newAuction).ConfigureAwait(false);

        var eventId = Guid.NewGuid();
        var payload = new AuctionCreatedPayload(newAuction.Id, newAuction.OwnerRegionId.ToString(), newAuction.EndsAtUtc, vehSnap, now);
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var envelope = new EventEnvelope(eventId, _localRegion.ToString(), "AuctionCreated", newAuction.Id, json, CreatedAtUtc: now);

        await _store.AppendAsync(envelope).ConfigureAwait(false);
        await _outboxRepo.EnqueueAsync(Guid.NewGuid(), eventId, newAuction.Id, "Auction", envelope.EventType, json, now).ConfigureAwait(false);

        return newAuction;
    }

    public async Task<BidResult> PlaceBidAsync(string auctionIdStr, BidRequest request)
    {
        var now = DateTime.UtcNow;
        if (!Guid.TryParse(auctionIdStr, out var auctionId))
            return new BidResult(false, null, "Invalid auction id");

        var existingAuction = await _auctionRepo.GetAsync(auctionId, forUpdate: true).ConfigureAwait(false);
        if (existingAuction is null) return new BidResult(false, null, "Auction not found");
        if (existingAuction.State is not (AuctionState.Active or AuctionState.Ending)) return new BidResult(false, null, "Auction not accepting bids");
        if (now > existingAuction.EndsAtUtc) return new BidResult(false, null, "Auction already ended");

        // 1) Get next local sequence
        var nextSeq = await _bidOrderingService.GetNextBidSequenceAsync(auctionIdStr).ConfigureAwait(false);

        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            Amount = request.Amount,
            Sequence = nextSeq,
            SourceRegionId = _localRegion,
            CreatedAtUtc = now,
            PartitionFlag = false,
            UpdatedAtUtc = now
        };

        // 2) Insert bid
        if (await _bidRepo.ExistsAsync(auctionId, bid.SourceRegionId, bid.Sequence).ConfigureAwait(false))
            return new BidResult(false, null, "Duplicate sequence in source region");

        await _bidRepo.InsertAsync(bid).ConfigureAwait(false);

        // 3) CAS update auction amounts
        var expected = existingAuction.RowVersion;
        var newHigh = Math.Max(existingAuction.CurrentHighBid ?? 0m, bid.Amount);
        var updatedSucceeded = await _auctionRepo.TryUpdateAmountsAsync(auctionId, newHigh, nextSeq, expected).ConfigureAwait(false);
        if (!updatedSucceeded) return new BidResult(false, null, "Concurrency conflict");

        // 4) Build event and persist in EventStore + Outbox
        var eventId = Guid.NewGuid();
        var payload = new BidPlacedPayload(bid.Id, bid.AuctionId, bid.Amount, bid.Sequence, bid.SourceRegionId.ToString(), bid.CreatedAtUtc, bid.PartitionFlag);
        var json = JsonSerializer.Serialize(payload);

        var envelope = new EventEnvelope(
            eventId,
            _localRegion.ToString(),
            "BidPlaced",
            auctionId,
            json,
            CreatedAtUtc: now);

        await _store.AppendAsync(envelope).ConfigureAwait(false);
        await _outboxRepo.EnqueueAsync(
            outboxId: Guid.NewGuid(),
            eventId: eventId,
            auctionId: auctionId,
            aggregateType: "Auction",
            eventType: envelope.EventType,
            payloadJson: json,
            createdAtUtc: now).ConfigureAwait(false);

        return new BidResult(true, nextSeq, "Accepted");
    }

    public async Task<Auction> GetAuctionAsync(string auctionId, ConsistencyLevel consistency)
    {
        if (!Guid.TryParse(auctionId, out var parsedId))
            throw new ArgumentException("Invalid auction id.");

        return consistency switch
        {
            ConsistencyLevel.Strong => await _auctionRepo.GetAsync(parsedId)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("Auction not found"),
            ConsistencyLevel.Eventual => await _auctionReadReplica.GetFromReplicaAsync(parsedId)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("Auction not found (replica)"),
            _ => throw new ArgumentOutOfRangeException(nameof(consistency))
        };
    }

    public async Task<ReconciliationResult> ReconcileAuctionAsync(string auctionIdStr)
    {
        if (!Guid.TryParse(auctionIdStr, out var auctionId))
            throw new ArgumentException("invalid id");

        var existingAuction = await _auctionRepo.GetAsync(auctionId).ConfigureAwait(false);
        if (existingAuction is null) throw new InvalidOperationException("Auction not found");

        var reconciliationRecord = await _reconciliationCheckpointRepo.GetAsync(auctionId).ConfigureAwait(false);
        var since = await _store.ResolveCreatedAtAsync(reconciliationRecord?.LastEventId).ConfigureAwait(false);

        var events = await _store.QuerySinceAsync(auctionId, since).ConfigureAwait(false);

        var knownBids = await _bidRepo.GetAllForAuctionAsync(auctionId).ConfigureAwait(false);

        var winner = ConflictResolver.DecideWinner(existingAuction, knownBids);
        await _auctionRepo.SaveWinnerAsync(auctionId, winner).ConfigureAwait(false);

        var last = events.LastOrDefault();
        await _reconciliationCheckpointRepo.UpsertAsync(auctionId, last?.EventId, DateTime.UtcNow).ConfigureAwait(false);

        return new ReconciliationResult(auctionId, winner);
    }

    public async Task ActivateAsync(Guid auctionId)
    {
        var now = DateTime.UtcNow;
        var existingAuction = await _auctionRepo.GetAsync(auctionId, forUpdate: true)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Auction not found");
        if (existingAuction.State is AuctionState.Ended or AuctionState.Cancelled)
            throw new InvalidOperationException("Cannot activate a finished auction");

        existingAuction.State = AuctionState.Active;
        existingAuction.UpdatedAtUtc = now;
        await _auctionRepo.InsertAsync(existingAuction).ConfigureAwait(false);

        var eventId = Guid.NewGuid();
        var payload = new AuctionActivatedPayload(existingAuction.Id, existingAuction.OwnerRegionId.ToString(), existingAuction.EndsAtUtc, now);
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var envelope = new EventEnvelope(eventId, _localRegion.ToString(), "AuctionActivated", existingAuction.Id, json, CreatedAtUtc: now);

        await _store.AppendAsync(envelope).ConfigureAwait(false);
        await _outboxRepo.EnqueueAsync(Guid.NewGuid(), eventId, existingAuction.Id, "Auction", envelope.EventType, json, now).ConfigureAwait(false);
    }
}
