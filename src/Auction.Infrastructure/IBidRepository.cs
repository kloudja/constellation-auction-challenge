using Domain.Model;

namespace Infrastructure;

public interface IBidRepository
{
    Task InsertAsync(Bid bid, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid auctionId, Region sourceRegionId, long sequence, CancellationToken ct = default);
    Task<IReadOnlyList<Bid>> GetAllForAuctionAsync(Guid auctionId, CancellationToken ct = default);
}
