using FluentAssertions;
using Xunit;
using Domain.Model;
using Infrastructure.InMemory;
using Services;

namespace UnitTests;

public class BidOrderingServiceTests
{
    [Fact(DisplayName = "Assigns monotonic sequence per auction")]
    public async Task Assigns_Monotonic_Sequence()
    {
        var bidRepository = new InMemoryBidRepository();
        var bidOrderingService = new BidOrderingService(bidRepository);

        var auctionId = Guid.NewGuid();
        var auctionIdStr = auctionId.ToString();

        // 1st sequence
        var seq1 = await bidOrderingService.GetNextBidSequenceAsync(auctionIdStr);

        // Persist a bid with seq1 so the service can compute the next from repo state
        await bidRepository.InsertAsync(new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            Amount = 100m,
            Sequence = seq1,
            SourceRegionId = Region.US,
            CreatedAtUtc = DateTime.UtcNow
        });

        // 2nd sequence should advance
        var seq2 = await bidOrderingService.GetNextBidSequenceAsync(auctionIdStr);

        seq1.Should().Be(1);
        seq2.Should().Be(2);
    }

    [Fact(DisplayName = "Sequences are independent per auction")]
    public async Task Sequences_Are_Independent()
    {
        var bidRepository = new InMemoryBidRepository();
        var bidOrderingService = new BidOrderingService(bidRepository);

        var auction1 = Guid.NewGuid();
        var auction2 = Guid.NewGuid();

        // Advance auction1 to seq=1 by inserting a bid
        var a1s1 = await bidOrderingService.GetNextBidSequenceAsync(auction1.ToString());
        await bidRepository.InsertAsync(new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auction1,
            Amount = 50m,
            Sequence = a1s1,
            SourceRegionId = Region.US,
            CreatedAtUtc = DateTime.UtcNow
        });

        // First sequence for auction2 should still be 1
        var a2s1 = await bidOrderingService.GetNextBidSequenceAsync(auction2.ToString());
        a2s1.Should().Be(1);
    }
}
