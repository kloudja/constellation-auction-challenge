namespace Auction.Domain;

public record BidResult(bool Accepted, long? Sequence, string Reason);
