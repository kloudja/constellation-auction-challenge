using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using FluentAssertions;
using Xunit;

namespace SimulationTests;

public class AuctionEndsDuringPartitionTests
{
    [Fact(DisplayName = "Bids created before EndsAtUtc during partition are accepted after heal; later bids are ignored in reconciliation")]
    public void Ends_During_Partition_Reconcile_Cutoff()
    {
        // Minimal “clocked” simulation: you can adapt to your RegionRuntime if present
        var now = DateTime.UtcNow;
        var ends = now.AddSeconds(1);

        var sim = new MiniAuctionSim(ends);
        sim.Partition = true;

        var b1 = sim.PlaceBid(amount: 300, createdAt: now.AddMilliseconds(500)); // before end
        var b2 = sim.PlaceBid(amount: 320, createdAt: now.AddSeconds(2));       // after end (late)

        sim.Partition = false; // heal
        var winner = sim.Reconcile();

        winner.Should().Be(b1, "only bids with CreatedAtUtc <= EndsAtUtc are considered valid after heal");
    }

    private sealed class MiniAuctionSim
    {
        private readonly DateTime _endsAt;
        public bool Partition { get; set; }
        private readonly List<(Guid id, decimal amount, DateTime created)> _local = new();
        public MiniAuctionSim(DateTime endsAt) => _endsAt = endsAt;
        public Guid PlaceBid(decimal amount, DateTime createdAt) { var id = Guid.NewGuid(); _local.Add((id, amount, createdAt)); return id; }
        public Guid? Reconcile() => _local.Where(b => b.created <= _endsAt).OrderByDescending(b => b.amount).Select(b => b.id).FirstOrDefault();
    }
}

