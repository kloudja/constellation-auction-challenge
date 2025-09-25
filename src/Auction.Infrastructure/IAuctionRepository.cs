using Domain.Model;

namespace Infrastructure;

public interface IAuctionRepository
{
    Task<Auction?> GetAsync(Guid id, bool forUpdate = false, CancellationToken ct = default);
    Task InsertAsync(Auction auction, CancellationToken ct = default);

    Task<bool> TryUpdateAmountsAsync(Guid id, decimal newHigh, long newSeq, long expectedRowVersion, CancellationToken ct = default);

    Task SaveWinnerAsync(Guid auctionId, Guid? winnerBidId, CancellationToken ct = default);
}
