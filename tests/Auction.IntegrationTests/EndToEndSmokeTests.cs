using Domain;
using Domain.Model;
using Eventing;
using FluentAssertions;
using Infrastructure.InMemory;
using Services;
using Sync;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

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
        var usAuctionRepository = new InMemoryAuctionRepository();
        var usBidRepository = new InMemoryBidRepository();
        var usEventStore = new InMemoryEventStoreRepository();
        var usOutboxRepository = new InMemoryOutboxRepository();
        var usAppliedEvents = new InMemoryAppliedEventRepository();
        var usReconciliationRepository = new InMemoryReconciliationCheckpointRepository();
        var usReadReplica = new InMemoryLaggedAuctionReplica(usAuctionRepository, TimeSpan.FromMilliseconds(500));

        // EU infra
        var euAuctionRepository = new InMemoryAuctionRepository();
        var euBidRepository = new InMemoryBidRepository();
        var euEventStore = new InMemoryEventStoreRepository();
        var euOutboxRepository = new InMemoryOutboxRepository();
        var euAppliedEvents = new InMemoryAppliedEventRepository();
        var euReconciliationRepository = new InMemoryReconciliationCheckpointRepository();
        var euReadReplica = new InMemoryLaggedAuctionReplica(usAuctionRepository, TimeSpan.FromMilliseconds(500));

        // Services
        var usBidOrderingService = new BidOrderingService(usBidRepository);
        var usAuctionService = new AuctionService("US", usAuctionRepository, usBidRepository, usBidOrderingService, usEventStore, usOutboxRepository, usReconciliationRepository, usReadReplica);
        var usPublisher = new EventPublisher("US", usOutboxRepository, usEventStore, busUS);
        var usDatabaseSyncService = new DatabaseSyncService("US", busUS, link, usAppliedEvents, usBidRepository, usAuctionRepository, usEventStore);

        var euBidOrderingService = new BidOrderingService(euBidRepository);
        var euAuctionService = new AuctionService("EU", euAuctionRepository, euBidRepository, euBidOrderingService, euEventStore, euOutboxRepository, euReconciliationRepository, euReadReplica);
        var euPublisher = new EventPublisher("EU", euOutboxRepository, euEventStore, busEU);
        var euDatabaseSyncService = new DatabaseSyncService("EU", busEU, link, euAppliedEvents, euBidRepository, euAuctionRepository, euEventStore);

        // 1) US creates auction (emits AuctionCreated)
        var a = await usAuctionService.CreateAuctionAsync(new CreateAuctionRequest(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(2)));

        // 2) US publishes and EU consumes AuctionCreated (EU now has a mirror in Draft)
        await usPublisher.PublishPendingAsync();
        await euDatabaseSyncService.DrainAndApplyAsync();

        (await euAuctionRepository.GetAsync(a.Id)).Should().NotBeNull();

        // 3) US activates auction (emits AuctionActivated), publish → EU apply (EU mirror goes Active)
        await usAuctionService.ActivateAsync(a.Id);
        await usPublisher.PublishPendingAsync();
        await euDatabaseSyncService.DrainAndApplyAsync();

        var mirror = await euAuctionRepository.GetAsync(a.Id);
        mirror!.State.Should().Be(AuctionState.Active);

        // 4) Partition starts AFTER both sides know the auction is Active
        link.SetState(LinkState.Partitioned);

        // 5) Place bids on both sides while partitioned
        (await usAuctionService.PlaceBidAsync(a.Id.ToString(), new BidRequest(310, "US"))).Accepted.Should().BeTrue();
        (await euAuctionService.PlaceBidAsync(a.Id.ToString(), new BidRequest(300, "EU"))).Accepted.Should().BeTrue();

        // 6) Publish locally (events buffered in inter-region channel)
        await usPublisher.PublishPendingAsync();
        await euPublisher.PublishPendingAsync();

        // 7) Heal link and drain/apply on both sides (idempotent)
        link.SetState(LinkState.Connected);
        await euDatabaseSyncService.DrainAndApplyAsync();
        await usDatabaseSyncService.DrainAndApplyAsync();

        // 8) Reconcile on owner (US)
        var result = await usAuctionService.ReconcileAuctionAsync(a.Id.ToString());
        result.WinnerBidId.Should().NotBeNull();

        var allBidsUS = await usBidRepository.GetAllForAuctionAsync(a.Id);
        var winner = allBidsUS.Single(b => b.Id == result.WinnerBidId);
        winner.Amount.Should().Be(310m);
    }
}
