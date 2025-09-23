using System;

namespace Domain;

public record CreateAuctionRequest(Guid VehicleId, DateTime EndsAtUtc);
