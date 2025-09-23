using System;

namespace Auction.Domain;

public record ReconciliationResult(Guid AuctionId, Guid? WinnerBidId);
