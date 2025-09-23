using Domain;
using Domain.Events;
using Eventing;
using Infrastructure.InMemory;
using Services;
using Sync;
using Domain;
using Eventing;
using FluentAssertions;
using Services;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using System.Linq;

namespace IntegrationTests;

public class EndToEndSmokeTests
{
    [Fact]
    public async Task PlaceBid_Publish_Forward_Apply_Reconcile()
    {
        // Regions: US & EU containers
        var busUS = new InMemoryEventBus();
        var busEU = new InMemoryEventBus();
        var link = new InterRegionChannel();

        // US infra
        var usAuc = new InMemoryAuctionRepository();
        var usBid = new InMemoryBidRepository();
        var usStore = new InMemoryEventStoreRepository();
        var usOutbox = new InMemoryOutboxRepository();
        var usApplied = new InMemoryAppliedEventRepository();
        var usCp = new InMemoryReconciliationCheckpointRepository();

        // EU infra
        var euAuc = new InMemoryAuctionRepository();
        var euBid = new InMemoryBidRepository();
        var euStore = new InMemoryEventStoreRepository();
        var euOutbox = new InMemoryOutboxRepository();
        var euApplied = new InMemoryAppliedEventRepository();
        var euCp = new InMemoryReconciliationCheckpointRepository();

        // Services
        var usOrdering = new BidOrderingService();
        var usSvc = new AuctionService("US", usAuc, usBid, usOrdering, usStore, usOutbox, usCp);
        var usPublisher = new EventPublisher("US", usOutbox, usStore, busUS);
        var usSync = new DatabaseSyncService("US", busUS, link, usApplied, usBid, usAuc, usStore);

        var euOrdering = new BidOrderingService();
        var euSvc = new AuctionService("EU", euAuc, euBid, euOrdering, euStore, euOutbox, euCp);
        var euPublisher = new EventPublisher("EU", euOutbox, euStore, busEU);
        var euSync = new DatabaseSyncService("EU", busEU, link, euApplied, euBid, euAuc, euStore);

        // Seed auction in US
        var a = await usSvc.CreateAuctionAsync(new CreateAuctionRequest(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(2)));
        a.State = Domain.Model.AuctionState.Active; // shortcut for the test
        await usAuc.InsertAsync(a); // persist state

        // Partition
        link.SetState(LinkState.Partitioned);

        // Place bids: US(310), EU(300) on the same auction
        (await usSvc.PlaceBidAsync(a.Id.ToString(), new BidRequest(310, "US"))).Accepted.Should().BeTrue();
        (await euSvc.PlaceBidAsync(a.Id.ToString(), new BidRequest(300, "EU"))).Accepted.Should().BeTrue();

        // Publish locally (still partitioned; events buffered in the channel)
        await usPublisher.PublishPendingAsync();
        await euPublisher.PublishPendingAsync();

        // Heal
        link.SetState(LinkState.Connected);

        // Drain + apply on both sides (idempotent)
        await usSync.DrainAndApplyAsync();
        await euSync.DrainAndApplyAsync();

        // Reconcile on owner (US)
        var result = await usSvc.ReconcileAuctionAsync(a.Id.ToString());
        result.WinnerBidId.Should().NotBeNull();

        // Winner should be the US(310) bid as per deterministic rules
        var allBidsUS = await usBid.GetAllForAuctionAsync(a.Id);
        var winner = allBidsUS.Single(b => b.Id == result.WinnerBidId);
        winner.Amount.Should().Be(310m);
    }
}
