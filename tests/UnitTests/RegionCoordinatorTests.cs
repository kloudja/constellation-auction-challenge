using Sync;
using Domain.Abstractions;
using FluentAssertions;
using Xunit;

namespace UnitTests;

public class RegionCoordinatorTests
{
    [Fact(DisplayName = "PartitionDetected and PartitionHealed events fire with correct args")]
    public async Task Events_Fire_On_State_Transitions()
    {
        RegionCoordinator regionCoordinator = new();

        List<PartitionChangedEventArgs> detected = new();
        List<PartitionChangedEventArgs> healed = new();

        regionCoordinator.PartitionDetected += (_, e) => detected.Add(e);
        regionCoordinator.PartitionHealed += (_, e) => healed.Add(e);

        (await regionCoordinator.GetPartitionStatusAsync()).IsPartitioned.Should().BeFalse();

        regionCoordinator.SetPartitioned();
        (await regionCoordinator.GetPartitionStatusAsync()).IsPartitioned.Should().BeTrue();
        detected.Should().HaveCount(1);
        detected[0].FromState.Should().Be("Connected");
        detected[0].ToState.Should().Be("Partitioned");

        (await regionCoordinator.IsRegionReachableAsync("US")).Should().BeFalse();
        (await regionCoordinator.IsRegionReachableAsync("EU")).Should().BeFalse();

        regionCoordinator.SetConnected();
        (await regionCoordinator.GetPartitionStatusAsync()).IsPartitioned.Should().BeFalse();
        healed.Should().HaveCount(1);
        healed[0].FromState.Should().Be("Partitioned");
        healed[0].ToState.Should().Be("Connected");

        (await regionCoordinator.IsRegionReachableAsync("US")).Should().BeTrue();
        (await regionCoordinator.IsRegionReachableAsync("EU")).Should().BeTrue();
    }

    [Fact(DisplayName = "ExecuteInRegionAsync throws when region is unreachable")]
    public async Task ExecuteInRegionAsync_Throws_When_Unreachable()
    {
        RegionCoordinator regionCoordinator = new();
        regionCoordinator.SetPartitioned();

        Func<Task<int>> op = () => regionCoordinator.ExecuteInRegionAsync<int>(
            "EU",
            async ct => { await Task.Delay(1, ct); return 42; });

        await op.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unreachable due to partition*");

        regionCoordinator.SetConnected();

        var result = await regionCoordinator.ExecuteInRegionAsync<int>(
            "EU",
            async ct => { await Task.Delay(1, ct); return 42; });
        result.Should().Be(42);
    }
}
