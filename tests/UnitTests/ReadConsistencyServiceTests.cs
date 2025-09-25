using Domain;
using Infrastructure.InMemory;
using Services;
using FluentAssertions;
using Xunit;
using Domain.Model;

namespace UnitTests;

public class ReadConsistencyServiceTests
{
    [Fact(DisplayName = "Strong reads reflect latest write; Eventual may be stale until lag passes")]
    public async Task Strong_Vs_Eventual_Reads_Behave_As_Expected()
    {
        InMemoryAuctionRepository writeAuctionRepository = new();
        InMemoryBidRepository writeBidRepository = new();
        InMemoryEventStoreRepository eventStoreRepository = new();
        InMemoryOutboxRepository eventOutboxRepository = new();
        InMemoryReconciliationCheckpointRepository reconciliationCheckpointRepository = new();
        InMemoryLaggedAuctionReplica readReplica = new(writeAuctionRepository, lag: TimeSpan.FromMilliseconds(500));
        InMemoryVehicleRepository inMemoryVehicleRepository = new();
        var newVehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            RegionId = "US",
            VehicleType = "Sedan",
            Make = "Toyota",
            Model = "Camry",
            Year = 2023,
            CreatedAtUtc = DateTime.UtcNow
        };
        await inMemoryVehicleRepository.InsertAsync(newVehicle);


        BidOrderingService bidOrderingService = new(writeBidRepository);
        AuctionService auctionService = new(
            localRegion: "US",
            auctionRepo: writeAuctionRepository,
            bidRepo: writeBidRepository,
            ordering: bidOrderingService,
            store: eventStoreRepository,
            outbox: eventOutboxRepository,
            cp: reconciliationCheckpointRepository,
            auctionReadReplica: readReplica,
            inMemoryVehicleRepository);

        // Create and activate auction
        var createdAuction = await auctionService.CreateAuctionAsync(new CreateAuctionRequest(newVehicle.Id, DateTime.UtcNow.AddMinutes(5)));
        await auctionService.ActivateAsync(createdAuction.Id);

        var firstBidResult = await auctionService.PlaceBidAsync(createdAuction.Id.ToString(), new BidRequest(100, "US"));
        firstBidResult.Accepted.Should().BeTrue();

        var strongRead1 = await auctionService.GetAuctionAsync(createdAuction.Id.ToString(), ConsistencyLevel.Strong);
        strongRead1.CurrentHighBid.Should().Be(100m);

        var eventualRead1 = await auctionService.GetAuctionAsync(createdAuction.Id.ToString(), ConsistencyLevel.Eventual);
        eventualRead1.CurrentHighBid.Should().NotBeNull();

        var secondBidResult = await auctionService.PlaceBidAsync(createdAuction.Id.ToString(), new BidRequest(120, "US"));
        secondBidResult.Accepted.Should().BeTrue();

        var strongRead2 = await auctionService.GetAuctionAsync(createdAuction.Id.ToString(), ConsistencyLevel.Strong);
        strongRead2.CurrentHighBid.Should().Be(120m);

        var eventualRead2_beforeLag = await auctionService.GetAuctionAsync(createdAuction.Id.ToString(), ConsistencyLevel.Eventual);
        eventualRead2_beforeLag.CurrentHighBid.Should().BeOneOf(100m, 120m);

        await Task.Delay(600);
        var eventualRead2_afterLag = await auctionService.GetAuctionAsync(createdAuction.Id.ToString(), ConsistencyLevel.Eventual);
        eventualRead2_afterLag.CurrentHighBid.Should().Be(120m, "replica should be refreshed after lag");
    }
}
