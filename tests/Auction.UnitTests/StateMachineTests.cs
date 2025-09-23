using Xunit;
using FluentAssertions;
using System;

namespace Auction.UnitTests;

// Simple placeholder domain type to illustrate transitions.
// Replace with your actual Auction aggregate and state machine.
public class AuctionStateMachineTests
{
    [Fact(DisplayName = "Active → Ending is allowed")]
    public void Active_To_Ending_Is_Allowed()
    {
        // arrange
        var auction = new Domain.Auction { Id = Guid.NewGuid(), State = "Active" };

        // act
        var allowed = CanTransition(auction.State, "Ending");

        // assert
        allowed.Should().BeTrue("ending flow must be valid per state machine (spec requirement).");
    }

    [Fact(DisplayName = "Draft → Ended is forbidden")]
    public void Draft_To_Ended_Is_Forbidden()
    {
        var a = new Domain.Auction { Id = Guid.NewGuid(), State = "Draft" };
        CanTransition(a.State, "Ended").Should().BeFalse();
    }

    private static bool CanTransition(string from, string to)
        => (from, to) switch
        {
            ("Draft", "Active") => true,
            ("Active", "Ending") => true,
            ("Ending", "Ended") => true,
            ("Active", "Ended") => true,   // e.g., forced end at deadline
            _ => false
        };
}