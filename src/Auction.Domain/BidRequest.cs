namespace Auction.Domain;

public record BidRequest(decimal Amount, string SourceRegionId);
