using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Domain.Model;
using Infrastructure.InMemory;

namespace UnitTests;

public class RowVersionConcurrencyTests
{
    [Fact(DisplayName = "Only one competing update succeeds when RowVersion matches")]
    public async Task Only_One_Update_Succeeds()
    {
        var auctionRepository = new InMemoryAuctionRepository();
        var auctionId = Guid.NewGuid();

        await auctionRepository.InsertAsync(new Auction
        {
            Id = auctionId,
            State = AuctionState.Active,
            CurrentHighBid = 100m,
            CurrentSeq = 41,
            RowVersion = 7
        });

        var okFirst = auctionRepository.TryUpdateCurrentHighBid(auctionId, expectedRowVersion: 7, newAmount: 120m, newSeq: 42);
        var okSecond = auctionRepository.TryUpdateCurrentHighBid(auctionId, expectedRowVersion: 7, newAmount: 130m, newSeq: 42);

        (okFirst ^ okSecond).Should().BeTrue("optimistic concurrency allows only one winner for a given rowversion");

        var reloaded = await auctionRepository.GetAsync(auctionId);
        reloaded!.RowVersion.Should().Be(8);
        reloaded.CurrentSeq.Should().Be(42);
        reloaded.CurrentHighBid.Should().BeOneOf(120m, 130m);
    }
}
