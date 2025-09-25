using Domain.Model;

namespace Services;

/// <summary>
/// Deterministic winner selection:
/// 1) Highest Amount
/// 2) If tie, earliest CreatedAtUtc
/// 3) If tie, prefer OwnerRegionId
/// 4) If tie, smallest BidId
/// </summary>
public static class ConflictResolver
{
    public static Guid? DecideWinner(Auction auction, IReadOnlyList<Bid> allBids)
    {
        var ordered = allBids
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.SourceRegionId == auction.OwnerRegionId ? 0 : 1)
            .ThenBy(b => b.Id)
            .ToList();

        return ordered.FirstOrDefault()?.Id;
    }
}
