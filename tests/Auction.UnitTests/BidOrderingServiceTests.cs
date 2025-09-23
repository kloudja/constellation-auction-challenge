using Xunit;
using FluentAssertions;
using Auction.Domain;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace Auction.UnitTests;

// Demonstrates per-auction monotonic sequence assignment.
public class BidOrderingServiceTests
{
    [Fact(DisplayName = "Assigns monotonic sequence per auction")]
    public async Task Assigns_Monotonic_Sequence()
    {
        var auctionId = Guid.NewGuid().ToString();
        var svc = new InMemoryBidOrderingService();

        var s1 = await svc.GetNextBidSequenceAsync(auctionId);
        var s2 = await svc.GetNextBidSequenceAsync(auctionId);

        s1.Should().Be(1);
        s2.Should().Be(2);
    }

    [Fact(DisplayName = "Sequences are independent per auction")]
    public async Task Sequences_Are_Independent()
    {
        var svc = new InMemoryBidOrderingService();

        var a1 = Guid.NewGuid().ToString();
        var a2 = Guid.NewGuid().ToString();

        await svc.GetNextBidSequenceAsync(a1); // 1
        var x = await svc.GetNextBidSequenceAsync(a2); // 1

        x.Should().Be(1);
    }

    private sealed class InMemoryBidOrderingService : IBidOrderingService
    {
        private readonly Dictionary<string,long> _seq = new();

        public Task<long> GetNextBidSequenceAsync(string auctionId)
        {
            if (!_seq.TryGetValue(auctionId, out var v)) v = 0;
            v++;
            _seq[auctionId] = v;
            return Task.FromResult(v);
        }

        public Task<bool> ValidateBidOrderAsync(string auctionId, Bid bid) =>
            Task.FromResult(_seq.TryGetValue(auctionId, out var v) ? bid.Sequence <= v : bid.Sequence == 1);

        public Task<IEnumerable<Bid>> GetOrderedBidsAsync(string auctionId, DateTime? since = null) =>
            Task.FromResult(Enumerable.Empty<Bid>());
    }
}
