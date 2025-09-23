# Design Decisions (CAP, Consistency, Indexing)

- Strong within a region for bidding operations; eventual across regions with reconciliation after partitions.
- Optimistic concurrency (RowVersion) on Auction rows; per-auction monotonic sequence for ordering.
- Indexing & constraints:
  - UNIQUE (AuctionId, Sequence) on `BID`
  - IX on (AuctionId, CreatedAtUtc) for reads
  - PK(EventId) for `EVENT_STORE`, PK(EventId) for `APPLIED_EVENT`
- Outbox â†’ Publisher pattern for reliable event emission; replay via `EVENT_STORE`.
