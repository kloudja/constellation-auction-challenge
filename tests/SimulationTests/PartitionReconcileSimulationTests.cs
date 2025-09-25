using Domain;
using Domain.Model;
using Eventing;
using FluentAssertions;
using Infrastructure.InMemory;
using Services;
using Sync;
using Xunit;

namespace SimulationTests;

public class PartitionReconcileSimulationTests
{
    [Fact(DisplayName = "No bids lost; correct winner after heal + reconcile")]
    public async Task Partition_Heal_Reconcile_Winner_Is_Correct()
    {
        // Cross-region plumbing
        var busUS = new InMemoryEventBus();
        var busEU = new InMemoryEventBus();
        var link = new InterRegionChannel();

        // ---------- US infrastructure ----------
        var usAuctionRepository = new InMemoryAuctionRepository();
        var usBidRepository = new InMemoryBidRepository();
        var usEventStore = new InMemoryEventStoreRepository();
        var usOutboxRepository = new InMemoryOutboxRepository();
        var usAppliedEvents = new InMemoryAppliedEventRepository();
        var usReconciliationRepository = new InMemoryReconciliationCheckpointRepository();
        var usVehicleRepository = new InMemoryVehicleRepository();
        var usReadReplica = new InMemoryLaggedAuctionReplica(usAuctionRepository, TimeSpan.FromMilliseconds(200));

        var usBidOrderingService = new BidOrderingService(usBidRepository);
        var usAuctionService = new AuctionService(
            "US",
            usAuctionRepository,
            usBidRepository,
            usBidOrderingService,
            usEventStore,
            usOutboxRepository,
            usReconciliationRepository,
            usReadReplica,
            usVehicleRepository);

        var usPublisher = new EventPublisher("US", usOutboxRepository, usEventStore, busUS);
        var usDatabaseSyncService = new DatabaseSyncService("US", busUS, link, usAppliedEvents, usBidRepository, usAuctionRepository, usEventStore, usBidOrderingService);

        // ---------- EU infrastructure ----------
        var euAuctionRepository = new InMemoryAuctionRepository();
        var euBidRepository = new InMemoryBidRepository();
        var euEventStore = new InMemoryEventStoreRepository();
        var euOutboxRepository = new InMemoryOutboxRepository();
        var euAppliedEvents = new InMemoryAppliedEventRepository();
        var euReconciliationRepository = new InMemoryReconciliationCheckpointRepository();
        var euVehicleRepository = new InMemoryVehicleRepository();
        var euReadReplica = new InMemoryLaggedAuctionReplica(euAuctionRepository, TimeSpan.FromMilliseconds(200));

        var euBidOrderingService = new BidOrderingService(euBidRepository);
        var euAuctionService = new AuctionService(
            "EU",
            euAuctionRepository,
            euBidRepository,
            euBidOrderingService,
            euEventStore,
            euOutboxRepository,
            euReconciliationRepository,
            euReadReplica,
            euVehicleRepository);

        var euPublisher = new EventPublisher("EU", euOutboxRepository, euEventStore, busEU);
        var euDatabaseSyncService = new DatabaseSyncService("EU", busEU, link, euAppliedEvents, euBidRepository, euAuctionRepository, euEventStore, euBidOrderingService);

        // ---------- Arrange vehicle + auction ----------
        var usVehicleService = new VehicleService(usVehicleRepository);
        var usVehicle = await usVehicleService.CreateAsync(new CreateVehicleRequest("US", "SUV", "Toyota", "RAV4", 2022));

        var auction = await usAuctionService.CreateAuctionAsync(new CreateAuctionRequest(usVehicle.Id, DateTime.UtcNow.AddMinutes(1)));

        // Publish AuctionCreated -> EU mirror (Draft)
        await usPublisher.PublishPendingAsync();
        await euDatabaseSyncService.DrainAndApplyAsync();
        (await euAuctionRepository.GetAsync(auction.Id)).Should().NotBeNull();

        // Activate in US -> publish -> EU apply (Active on both)
        await usAuctionService.ActivateAsync(auction.Id);
        await usPublisher.PublishPendingAsync();
        await euDatabaseSyncService.DrainAndApplyAsync();
        (await euAuctionRepository.GetAsync(auction.Id))!.State.Should().Be(AuctionState.Active);

        // ---------- Partition ----------
        link.SetState(LinkState.Partitioned);

        // Bids on both sides while partitioned
        (await usAuctionService.PlaceBidAsync(auction.Id.ToString(), new BidRequest(310, "US"))).Accepted.Should().BeTrue();
        (await euAuctionService.PlaceBidAsync(auction.Id.ToString(), new BidRequest(300, "EU"))).Accepted.Should().BeTrue();

        // Publish locally (buffered cross-region)
        await usPublisher.PublishPendingAsync();
        await euPublisher.PublishPendingAsync();

        // ---------- Heal & apply on both sides ----------
        link.SetState(LinkState.Connected);
        await usDatabaseSyncService.DrainAndApplyAsync();
        await euDatabaseSyncService.DrainAndApplyAsync();

        // ---------- Reconcile at owner (US) ----------
        var reconciliation = await usAuctionService.ReconcileAuctionAsync(auction.Id.ToString());
        reconciliation.WinnerBidId.Should().NotBeNull();

        var bidsUS = await usBidRepository.GetAllForAuctionAsync(auction.Id);
        var winner = bidsUS.Single(b => b.Id == reconciliation.WinnerBidId);
        winner.Amount.Should().Be(310m, "deterministic rule prefers higher amount (then tie-breakers)");

        // Ensure the EU bid was not lost
        bidsUS.Should().ContainSingle(b => b.SourceRegionId.ToString() == "EU" && b.Amount == 300m);
    }

}
