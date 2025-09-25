-- REGION
IF OBJECT_ID('dbo.Region','U') IS NOT NULL DROP TABLE dbo.Region;
CREATE TABLE dbo.Region (
  Id           NVARCHAR(8)   NOT NULL CONSTRAINT PK_Region PRIMARY KEY,
  Name         NVARCHAR(100) NOT NULL,
  CreatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_Region_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_Region_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc DATETIME2(3)  NULL
);

-- VEHICLE
IF OBJECT_ID('dbo.Vehicle','U') IS NOT NULL DROP TABLE dbo.Vehicle;
CREATE TABLE dbo.Vehicle (
  Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Vehicle PRIMARY KEY,
  RegionId       NVARCHAR(8) NOT NULL CONSTRAINT FK_Vehicle_Region REFERENCES dbo.Region(Id),
  VehicleType    NVARCHAR(20) NOT NULL
      CONSTRAINT CK_Vehicle_Type CHECK (VehicleType IN ('Sedan','SUV','Hatchback','Truck')),
  Make           NVARCHAR(80) NOT NULL,
  Model          NVARCHAR(80) NOT NULL,
  Year           INT NOT NULL CHECK (Year BETWEEN 1900 AND 2100),
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Vehicle_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Vehicle_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL
);

-- AUCTION
IF OBJECT_ID('dbo.Auction','U') IS NOT NULL DROP TABLE dbo.Auction;
CREATE TABLE dbo.Auction (
  Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Auction PRIMARY KEY,
  VehicleId      UNIQUEIDENTIFIER NULL CONSTRAINT FK_Auction_Vehicle REFERENCES dbo.Vehicle(Id),
  OwnerRegionId  NVARCHAR(8) NOT NULL CONSTRAINT FK_Auction_Region REFERENCES dbo.Region(Id),
  State          NVARCHAR(20) NOT NULL CHECK (State IN ('Draft','Active','Ending','Ended','Cancelled')),
  EndsAtUtc      DATETIME2(3) NOT NULL,
  CurrentHighBid DECIMAL(18,2) NULL,
  CurrentSeq     BIGINT NOT NULL DEFAULT 0, -- local sequence in owner region
  RowVersion     ROWVERSION NOT NULL,       -- optimistic concurrency token
  WinnerBidId    UNIQUEIDENTIFIER NULL,
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Auction_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Auction_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL
);

-- BID
IF OBJECT_ID('dbo.Bid','U') IS NOT NULL DROP TABLE dbo.Bid;
CREATE TABLE dbo.Bid (
  Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Bid PRIMARY KEY,
  AuctionId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Bid_Auction REFERENCES dbo.Auction(Id),
  SourceRegionId NVARCHAR(8) NOT NULL CONSTRAINT FK_Bid_Region REFERENCES dbo.Region(Id),
  Amount         DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
  Sequence       BIGINT NOT NULL,
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Bid_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Bid_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL,
  PartitionFlag  BIT NOT NULL DEFAULT 0,
  BidderId       NVARCHAR(64) NULL,
  CONSTRAINT UQ_Bid_Auction_Source_Seq UNIQUE (AuctionId, SourceRegionId, Sequence)
);

-- EVENT_OUTBOX (publish-after-commit)
IF OBJECT_ID('dbo.EventOutbox','U') IS NOT NULL DROP TABLE dbo.EventOutbox;
CREATE TABLE dbo.EventOutbox (
  Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EventOutbox PRIMARY KEY,
  AggregateType  NVARCHAR(50) NOT NULL, -- 'Auction'
  AuctionId      UNIQUEIDENTIFIER NOT NULL,
  EventId        UNIQUEIDENTIFIER NOT NULL, -- identity in EventStore
  EventType      NVARCHAR(50) NOT NULL,     -- 'AuctionCreated','AuctionActivated','BidPlaced',...
  PayloadJson    NVARCHAR(MAX) NOT NULL,
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Outbox_CreatedAt DEFAULT SYSUTCDATETIME(),
  Published      BIT NOT NULL DEFAULT 0,
  PublishedAtUtc DATETIME2(3) NULL,
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Outbox_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL
);

-- EVENT_STORE (append-only + replay)
IF OBJECT_ID('dbo.EventStore','U') IS NOT NULL DROP TABLE dbo.EventStore;
CREATE TABLE dbo.EventStore (
  EventId          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EventStore PRIMARY KEY,
  ProducerRegionId NVARCHAR(8) NOT NULL CONSTRAINT FK_EventStore_Region REFERENCES dbo.Region(Id),
  AuctionId        UNIQUEIDENTIFIER NOT NULL,
  EventType        NVARCHAR(50) NOT NULL,
  PayloadJson      NVARCHAR(MAX) NOT NULL,
  CreatedAtUtc     DATETIME2(3) NOT NULL CONSTRAINT DF_EventStore_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc     DATETIME2(3) NOT NULL CONSTRAINT DF_EventStore_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc     DATETIME2(3) NULL
);

-- APPLIED_EVENT (idempotency ledger at destination)
IF OBJECT_ID('dbo.AppliedEvent','U') IS NOT NULL DROP TABLE dbo.AppliedEvent;
CREATE TABLE dbo.AppliedEvent (
  EventId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AppliedEvent PRIMARY KEY,
  AppliedAtUtc   DATETIME2(3) NOT NULL,
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_AppliedEvent_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_AppliedEvent_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL
);

-- RECONCILIATION_CP (checkpoint per auction for incremental replay)
IF OBJECT_ID('dbo.ReconciliationCp','U') IS NOT NULL DROP TABLE dbo.ReconciliationCp;
CREATE TABLE dbo.ReconciliationCp (
  AuctionId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ReconciliationCp PRIMARY KEY,
  LastEventId    UNIQUEIDENTIFIER NULL,
  LastRunAtUtc   DATETIME2(3) NULL,
  CreatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Recon_CreatedAt DEFAULT SYSUTCDATETIME(),
  UpdatedAtUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_Recon_UpdatedAt DEFAULT SYSUTCDATETIME(),
  DeletedAtUtc   DATETIME2(3) NULL
);

-- AUDIT TRAIL (required by spec)
IF OBJECT_ID('dbo.AuditLog','U') IS NOT NULL DROP TABLE dbo.AuditLog;
CREATE TABLE dbo.AuditLog (
  Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
  EntityType    NVARCHAR(50) NOT NULL,    -- 'Vehicle','Auction','Bid','Event'
  EntityId      UNIQUEIDENTIFIER NULL,
  Operation     NVARCHAR(30) NOT NULL,    -- 'Create','Update','Delete','ApplyEvent'
  RegionId      NVARCHAR(8) NULL,         
  PayloadJson   NVARCHAR(MAX) NULL,
  CreatedAtUtc  DATETIME2(3) NOT NULL CONSTRAINT DF_Audit_CreatedAt DEFAULT SYSUTCDATETIME()
);
