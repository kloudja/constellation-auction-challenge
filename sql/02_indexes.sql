-- Auction lookups by owner/state
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Auction_Owner_State' AND object_id=OBJECT_ID('dbo.Auction'))
CREATE NONCLUSTERED INDEX IX_Auction_Owner_State ON dbo.Auction (OwnerRegionId, State) INCLUDE (EndsAtUtc, CurrentHighBid, CurrentSeq);

-- Bid queries by auction and ordering (amount/created)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bid_Auction_CreatedAt' AND object_id=OBJECT_ID('dbo.Bid'))
CREATE NONCLUSTERED INDEX IX_Bid_Auction_CreatedAt ON dbo.Bid (AuctionId, CreatedAtUtc) INCLUDE (Amount, SourceRegionId, Sequence);

-- Optional composite to speed tie-breakers during reconciliation
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bid_Reconcile_Order' AND object_id=OBJECT_ID('dbo.Bid'))
CREATE NONCLUSTERED INDEX IX_Bid_Reconcile_Order ON dbo.Bid (AuctionId, Amount DESC, CreatedAtUtc, SourceRegionId, Id);

-- Outbox polling: pending rows in chronological order
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Outbox_Pending' AND object_id=OBJECT_ID('dbo.EventOutbox'))
CREATE NONCLUSTERED INDEX IX_Outbox_Pending ON dbo.EventOutbox (Published, CreatedAtUtc) INCLUDE (EventType, AuctionId, EventId);

-- EventStore replay by auction
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EventStore_Auction_Time' AND object_id=OBJECT_ID('dbo.EventStore'))
CREATE NONCLUSTERED INDEX IX_EventStore_Auction_Time ON dbo.EventStore (AuctionId, CreatedAtUtc) INCLUDE (EventId, ProducerRegionId, EventType);