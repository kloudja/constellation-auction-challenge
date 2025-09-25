-- VEHICLE: region-scoped lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Vehicle_Region_Type' AND object_id=OBJECT_ID('dbo.Vehicle'))
CREATE NONCLUSTERED INDEX IX_Vehicle_Region_Type ON dbo.Vehicle (RegionId, VehicleType) INCLUDE (Make, Model, Year);

-- AUCTION: common queries by owner/state and ends time
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Auction_Owner_State' AND object_id=OBJECT_ID('dbo.Auction'))
CREATE NONCLUSTERED INDEX IX_Auction_Owner_State ON dbo.Auction (OwnerRegionId, State) INCLUDE (EndsAtUtc, CurrentHighBid, CurrentSeq);

-- BID: history & reconciliation (ordering by amount/time/source)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bid_Auction_CreatedAt' AND object_id=OBJECT_ID('dbo.Bid'))
CREATE NONCLUSTERED INDEX IX_Bid_Auction_CreatedAt ON dbo.Bid (AuctionId, CreatedAtUtc) INCLUDE (Amount, SourceRegionId, Sequence);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Bid_Reconcile_Order' AND object_id=OBJECT_ID('dbo.Bid'))
CREATE NONCLUSTERED INDEX IX_Bid_Reconcile_Order ON dbo.Bid (AuctionId, Amount DESC, CreatedAtUtc, SourceRegionId, Id);

-- OUTBOX: efficient polling by published flag and creation time
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Outbox_Pending' AND object_id=OBJECT_ID('dbo.EventOutbox'))
CREATE NONCLUSTERED INDEX IX_Outbox_Pending ON dbo.EventOutbox (Published, CreatedAtUtc) INCLUDE (EventType, AuctionId, EventId);

-- EVENT_STORE: replay per auction
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_EventStore_Auction_Time' AND object_id=OBJECT_ID('dbo.EventStore'))
CREATE NONCLUSTERED INDEX IX_EventStore_Auction_Time ON dbo.EventStore (AuctionId, CreatedAtUtc) INCLUDE (EventId, ProducerRegionId, EventType);

-- AUDIT: by entity and time (useful for trails)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Audit_Entity_Time' AND object_id=OBJECT_ID('dbo.AuditLog'))
CREATE NONCLUSTERED INDEX IX_Audit_Entity_Time ON dbo.AuditLog (EntityType, EntityId, CreatedAtUtc);
