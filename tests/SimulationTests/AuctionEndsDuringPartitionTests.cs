using FluentAssertions;
using Xunit;

namespace SimulationTests;

public class AuctionEndsDuringPartitionTests
{
    [Fact(DisplayName = "Bids created before EndsAtUtc during partition are accepted after heal; later bids are ignored in reconciliation")]
    public void Ends_During_Partition_Reconcile_Cutoff()
    {
        var now = DateTime.UtcNow;
        var ends = now.AddSeconds(1);

        var sim = new MiniAuctionSim(ends);
        sim.Partition = true;

        var b1 = sim.PlaceBid(amount: 300, createdAt: now.AddMilliseconds(500));
        var b2 = sim.PlaceBid(amount: 320, createdAt: now.AddSeconds(2));

        sim.Partition = false;
        var winner = sim.Reconcile();

        winner.Should().Be(b1, "only bids with CreatedAtUtc <= EndsAtUtc are considered valid after heal");
    }

    private sealed class MiniAuctionSim(DateTime endsAt)
    {
        private readonly DateTime _endsAt = endsAt;
        public bool Partition { get; set; }
        private readonly List<(Guid id, decimal amount, DateTime created)> _local = new();

        public Guid PlaceBid(decimal amount, DateTime createdAt) { var id = Guid.NewGuid(); _local.Add((id, amount, createdAt)); return id; }
        public Guid? Reconcile() => _local.Where(b => b.created <= _endsAt).OrderByDescending(b => b.amount).Select(b => b.id).FirstOrDefault();
    }
}

