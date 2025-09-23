using System;

namespace Domain;

public record ReconciliationResult(Guid AuctionId, Guid? WinnerBidId);
