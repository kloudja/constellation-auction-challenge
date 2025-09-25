-- Optimistic update with rowversion check (CAS)
IF OBJECT_ID('dbo.Auction_UpdateAmounts','P') IS NOT NULL DROP PROCEDURE dbo.Auction_UpdateAmounts;
GO
CREATE PROCEDURE dbo.Auction_UpdateAmounts
  @AuctionId UNIQUEIDENTIFIER,
  @NewHigh   DECIMAL(18,2),
  @NewSeq    BIGINT,
  @ExpectedRowVersion BINARY(8)
AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.Auction
     SET CurrentHighBid = @NewHigh,
         CurrentSeq     = @NewSeq,
         UpdatedAtUtc   = SYSUTCDATETIME()
   WHERE Id         = @AuctionId
     AND RowVersion = @ExpectedRowVersion;

  SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- Outbox dequeue (simplificado; real life: UPDLOCK/READPAST/ROWLOCK/claim)
IF OBJECT_ID('dbo.usp_EventOutbox_DequeueTop','P') IS NOT NULL DROP PROCEDURE dbo.usp_EventOutbox_DequeueTop;
GO
CREATE PROCEDURE dbo.usp_EventOutbox_DequeueTop
  @BatchSize INT = 128
AS
BEGIN
  SET NOCOUNT ON;
  SELECT TOP (@BatchSize)
         Id, EventId, AuctionId, AggregateType, EventType, PayloadJson, CreatedAtUtc, Published, PublishedAtUtc
  FROM dbo.EventOutbox WITH (READPAST)
  WHERE Published = 0
  ORDER BY CreatedAtUtc ASC;
END
GO

IF OBJECT_ID('dbo.usp_EventOutbox_MarkPublished','P') IS NOT NULL DROP PROCEDURE dbo.usp_EventOutbox_MarkPublished;
GO
CREATE PROCEDURE dbo.usp_EventOutbox_MarkPublished
  @Id UNIQUEIDENTIFIER
AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.EventOutbox
     SET Published = 1,
         PublishedAtUtc = SYSUTCDATETIME(),
         UpdatedAtUtc   = SYSUTCDATETIME()
   WHERE Id = @Id;
END
GO

-- Optional: Audit insert helper
IF OBJECT_ID('dbo.usp_AuditLog_Insert','P') IS NOT NULL DROP PROCEDURE dbo.usp_AuditLog_Insert;
GO
CREATE PROCEDURE dbo.usp_AuditLog_Insert
  @EntityType NVARCHAR(50),
  @EntityId   UNIQUEIDENTIFIER = NULL,
  @Operation  NVARCHAR(30),
  @RegionId   NVARCHAR(8) = NULL,
  @PayloadJson NVARCHAR(MAX) = NULL
AS
BEGIN
  INSERT INTO dbo.AuditLog (Id, EntityType, EntityId, Operation, RegionId, PayloadJson, CreatedAtUtc)
  VALUES (NEWID(), @EntityType, @EntityId, @Operation, @RegionId, @PayloadJson, SYSUTCDATETIME());
END
GO
