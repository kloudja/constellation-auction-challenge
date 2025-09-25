# Constellation – Distributed Car Auction (Challenge Submission)

This repository implements a distributed, multi-region car auction platform designed for the engineering challenge.
It demonstrates solutions for CAP theorem trade-offs, network partitions, and multi-region consistency.

## Features
- Cross-region bidding during network partitions.
- Deterministic reconciliation after healing (no bids lost).
- Strong vs Eventual read consistency guarantees.
- Vehicle management: Sedan, SUV, Hatchback, Truck – region-scoped (no replication).
- Database design:
  - Optimistic concurrency (rowversion) for compare-and-swap updates.
  - Duplicate detection with UNIQUE (AuctionId, SourceRegionId, Sequence).

## How to Run

### Build and run tests
```bash
dotnet build
dotnet test
```

### Run only partition simulation tests
```bash
dotnet test --filter "FullyQualifiedName~Partition|Category=partition"
```

### Database integration tests (NOT TESTED)
Set an environment variable with your SQL Server connection string (LocalDB or full SQL Server). If TEST_SQL_CONNSTR is unset, DB integration tests are skipped.

Windows PowerShell:
```powershell
$env:TEST_SQL_CONNSTR="Server=(localdb)\MSSQLLocalDB;Database=AuctionDb;Trusted_Connection=True;"
dotnet test tests/Auction.IntegrationTests/Auction.IntegrationTests.csproj
```

## Folder Structure (high-level)
```
src/
  Auction.Domain/           # domain models & contracts (IAuctionService, IRegionCoordinator, etc.)
  Auction.Services/         # application services (AuctionService, VehicleService, ConflictResolver)
  Auction.Sync/             # region coordinator & cross-region sync (publisher/apply)
  Auction.Infrastructure/   # in-memory repos, lagged read replica, DB patterns
tests/
  Auction.UnitTests/        # unit tests (vehicles region-isolated, read consistency, conflict rules)
  Auction.IntegrationTests/ # DB integration tests (rowversion CAS, UNIQUE idempotency)
  Auction.SimulationTests/  # partition/heal/reconcile scenarios
docs/
  Architecture-and-Design.md
```

## Key Design Decisions

### CAP trade-off
- During a partition, the system prioritizes Availability (bids accepted in each region).
- On healing, Consistency is restored via deterministic reconciliation (event history plus tie-breakers).

### Outbox pattern
- Events produced in the owner region are shipped cross-region with at-least-once delivery.
- Destination regions use an AppliedEvent ledger for idempotency.

### Duplicate detection
- Database enforces UNIQUE (AuctionId, SourceRegionId, Sequence) on Bid to prevent duplicates across regions.

### Optimistic concurrency
- Auction.RowVersion is a SQL Server rowversion column used for compare-and-swap updates of CurrentHighBid and CurrentSeq.

### Strong vs Eventual reads
- GetAuctionAsync(Strong) targets the write database (authoritative state).
- GetAuctionAsync(Eventual) targets a lagged replica, which may be stale within a configured lag.

### Vehicle strategy
- Vehicles are region-scoped.
- Auctions store a Vehicle Snapshot (VehicleType, Make, Model, Year) in AuctionCreated for remote mirrors, avoiding cross-region replication of Vehicle tables.

## Limitations
- Single owner per auction (e.g., US in examples). Ownership transfer is out of scope.
- The read replica is simulated in-memory; the pattern generalizes to real replicas.
- No authentication or UI; focus is backend simulation, storage, and consistency semantics.
