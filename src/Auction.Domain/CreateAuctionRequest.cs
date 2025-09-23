using System;

namespace Auction.Domain;

public record CreateAuctionRequest(Guid VehicleId, DateTime EndsAtUtc);
