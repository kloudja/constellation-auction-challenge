using FluentAssertions;
using Xunit;
using Domain.Model;
using Infrastructure.InMemory;

namespace UnitTests;

public class ReadConsistencyTests
{
    [Fact(DisplayName = "Strong reflects latest; Eventual lags updates by configured period")]
    public async Task Strong_Vs_Eventual_Reads()
    {
        var writeRepository = new InMemoryAuctionRepository();
        var replica = new InMemoryLaggedAuctionReplica(writeRepository, TimeSpan.FromMilliseconds(500));

        var auctionId = Guid.NewGuid();
        await writeRepository.InsertAsync(new Auction
        {
            Id = auctionId,
            OwnerRegionId = Region.US,
            State = AuctionState.Draft,
            EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CurrentHighBid = 100m,
            RowVersion = 1
        });

        var snapshot1 = await replica.GetFromReplicaAsync(auctionId);
        snapshot1.Should().NotBeNull();
        snapshot1!.CurrentHighBid.Should().Be(100m);

        var strong = await writeRepository.GetAsync(auctionId);
        strong!.CurrentHighBid = 200m;
        strong.UpdatedAtUtc = DateTime.UtcNow;
        strong.RowVersion += 1;
        await writeRepository.InsertAsync(strong);

        var snapshot2 = await replica.GetFromReplicaAsync(auctionId);
        snapshot2.Should().NotBeNull();
        snapshot2!.CurrentHighBid.Should().Be(100m, "replica is stale within the configured lag");

        await Task.Delay(550);
        var snapshot3 = await replica.GetFromReplicaAsync(auctionId);
        snapshot3.Should().NotBeNull();
        snapshot3!.CurrentHighBid.Should().Be(200m, "replica refreshed after lag");
    }
}
