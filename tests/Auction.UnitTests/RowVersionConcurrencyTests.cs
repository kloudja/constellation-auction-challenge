using Xunit;
using FluentAssertions;
using System;
using Domain.Model;
using System.Collections.Generic;

namespace UnitTests;

public class RowVersionConcurrencyTests
{
    [Fact(DisplayName = "Only one competing update succeeds when RowVersion matches")]
    public void Only_One_Update_Succeeds()
    {
        var repo = new InMemoryAuctionRepo();
        var id = Guid.NewGuid();
        repo.Insert(new Auction { Id = id, CurrentHighBid = 100, CurrentSeq = 41, RowVersion = 7, State = AuctionState.Active });

        // Tx1 reads RowVersion=7
        var ok1 = repo.TryUpdateCurrentHighBid(id, expectedRowVersion: 7, newAmount: 120, newSeq: 42);
        // Tx2 also attempts with expectedRowVersion=7
        var ok2 = repo.TryUpdateCurrentHighBid(id, expectedRowVersion: 7, newAmount: 130, newSeq: 42);

        (ok1 ^ ok2).Should().BeTrue("exactly one should succeed due to optimistic concurrency.");
        var a = repo.Get(id);
        a.RowVersion.Should().Be(8);
        a.CurrentSeq.Should().Be(42);
        a.CurrentHighBid.Should().BeOneOf(120, 130);
    }

    private sealed class InMemoryAuctionRepo
    {
        private readonly Dictionary<Guid, Auction> _store = new();

        public void Insert(Auction a) => _store[a.Id] = a;

        public Auction Get(Guid id) => _store[id];

        public bool TryUpdateCurrentHighBid(Guid id, long expectedRowVersion, decimal newAmount, long newSeq)
        {
            var a = _store[id];
            if (a.RowVersion != expectedRowVersion) return false;
            a.CurrentHighBid = newAmount;
            a.CurrentSeq = newSeq;
            a.RowVersion += 1; // emulate DB rowversion bump
            _store[id] = a;
            return true;
        }
    }
}
