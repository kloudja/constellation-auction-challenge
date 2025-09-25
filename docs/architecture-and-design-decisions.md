# Architecture & Design

## 1) System Overview

**Problem:** enable auctions spanning **US** and **EU** regions. Users can place bids from any region. Network partitions may occur; we must **not lose bids** and must **converge** to a single winner once connectivity returns.

**High-level approach (AP during partition, converge after heal):**

* During a partition, **each region remains available** to accept local bids.
* After healing, the **owner region** (auction’s `OwnerRegionId`) **reconciles** all bids (local + remote) using a **deterministic total order**.
* **Vehicles are region-specific** (no replication). Remote mirrors of auctions do **not** read the Vehicle table; they use a **VehicleSnapshot** carried by `AuctionCreated`.

**Key principles**

* **At-least-once** delivery to remote regions (Outbox + idempotent apply).
* **Idempotency** via `AppliedEvent` and DB `UNIQUE (AuctionId, SourceRegionId, Sequence)`.
* **Optimistic concurrency (CAS)** using `rowversion` for the `Auction` record.
* **Strong vs Eventual** read paths are explicit per operation.
* **Separation of concerns:** Outbox (publication queue) vs EventStore (historical log).

---

## 2) Architecture (US / EU)

```mermaid
flowchart LR
  subgraph US[US Region (Owner example)]
    AUS[AuctionService]
    PUB[EventPublisher]
    WDB[(Write DB)]
    BUS[(EventBus/Channel Producer)]
  end

  subgraph EU[EU Region]
    AES[AuctionService Mirror]
    BSUB[DatabaseSyncService (Consumer/Applier)]
    RDB[(Mirror/Read DB)]
  end

  AUS -- Outbox->Publish --> PUB --> BUS
  BUS == cross-region ==>> BSUB
  BSUB --> RDB

  AUS --- WDB
  AES --- RDB
```

**Components**

* **AuctionService**: lifecycle (`Create/Activate/End/PlaceBid/Reconcile`) and **GetAuctionAsync(Strong|Eventual)**.
* **EventPublisher**: polls **EventOutbox** and publishes to a **Bus/Channel** (in tests, in-memory).
* **DatabaseSyncService**: consumes, **applies idempotently** (`AppliedEvent`) and appends to **EventStore** (destination side).
* **IRegionCoordinator / RegionCoordinator**: reports `PartitionStatus`, reachability, and provides `ExecuteInRegionAsync<T>`; raises `PartitionDetected/Healed`.
* **IAuctionReadReplica**: lagged replica for **Eventual** reads (first read may be stale).
* **VehicleService**: CRUD for region-scoped vehicles; used by `CreateAuctionAsync` to load snapshot.

---

## 3) Data Model (Core Tables & Constraints)

### Vehicle (region-specific)

* `Vehicle(Id, RegionId, VehicleType, Make, Model, Year, CreatedAtUtc, UpdatedAtUtc, DeletedAtUtc)`
  Types: **Sedan | SUV | Hatchback | Truck** (validated).
  **Not replicated** cross-region.

### Auction

* `Auction(Id, OwnerRegionId, State, EndsAtUtc, CurrentHighBid, CurrentSeq, RowVersion, WinnerBidId, CreatedAtUtc, UpdatedAtUtc)`
* Mirrors may store **VehicleSnapshot** columns or just use event payload on apply.
* **RowVersion** (SQL `rowversion`) for **CAS** on updates.

### Bid

* `Bid(Id, AuctionId, SourceRegionId, Sequence, Amount, CreatedAtUtc, PartitionFlag)`
* **UNIQUE (AuctionId, SourceRegionId, Sequence)** → duplicate detection across regions.
* `PartitionFlag` marks if the bid was produced while partitioned (diagnostics only).

### EventOutbox

* Queue for **publication** (at-least-once). `Published` flag for polling.

### EventStore

* **Append-only** historical log (source of truth for **replay/reconciliation**).

### AppliedEvent

* Destination ledger of **applied** `EventId` → makes apply **idempotent**.

### ReconciliationCheckpoint

* Per-auction marker (`LastEventId`) to replay incrementally.

**Indexes (essentials)**

* `Bid(AuctionId, Amount DESC, CreatedAtUtc, SourceRegionId, Id)` → deterministic winner scan.
* `Bid(AuctionId, SourceRegionId, Sequence) UNIQUE` → duplicate prevention.
* `EventOutbox(Published, CreatedAtUtc)` → efficient publisher polling.
* `EventStore(AuctionId, CreatedAtUtc)` → ordered replay by auction.
* `AppliedEvent(EventId PK)` → O(1) idempotency check.

---

## 4) Event Contracts

```csharp
// Vehicle snapshot only in Created (not in Activated)
public sealed record VehicleSnapshot(string VehicleType, string Make, string Model, int Year);

public sealed record AuctionCreatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    VehicleSnapshot Vehicle,
    DateTime CreatedAtUtc);

public sealed record AuctionActivatedPayload(
    Guid AuctionId,
    string OwnerRegionId,
    DateTime EndsAtUtc,
    DateTime CreatedAtUtc);

public sealed record BidPlacedPayload(
    Guid BidId,
    Guid AuctionId,
    decimal Amount,
    long Sequence,            // monotonic per (AuctionId, SourceRegionId)
    string SourceRegionId,
    DateTime CreatedAtUtc,
    bool PartitionFlag);
```

**Design notes**

* **Vehicle snapshot** travels only with `AuctionCreated`. Remote mirrors never read Vehicle table.
* `AuctionActivated` == **state change only**.
* `BidPlaced` carries **Sequence** (per `(AuctionId, SourceRegionId)`) and **CreatedAtUtc** used in reconciliation tie-breakers.

---

## 5) Consistency per Operation

| Operation             | Consistency                                      | Rationale                                                                             |
| --------------------- | ------------------------------------------------ | ------------------------------------------------------------------------------------- |
| Create Vehicle        | **Strong**                                       | Local to region; no cross-region reads.                                               |
| Create Auction        | **Strong**                                       | Validates `Vehicle.RegionId == OwnerRegionId`; emits `AuctionCreated` + snapshot.     |
| Activate Auction      | **Strong**                                       | Emits `AuctionActivated` (no vehicle payload).                                        |
| Place Bid             | **Strong (local write)** / **Eventual (global)** | Accept locally even if partitioned; remote view updates on heal.                      |
| View Auction (Owner)  | **Strong**                                       | Owner’s write DB is source of truth.                                                  |
| View Auction (Remote) | **Eventual**                                     | Mirror reads; may be stale within replica/apply lag.                                  |
| End Auction           | **Strong**                                       | Owner sets cutoff; reconciliation only accepts bids with `CreatedAtUtc <= EndsAtUtc`. |

**Read paths**

* `GetAuctionAsync(Strong)` → **write store**.
* `GetAuctionAsync(Eventual)` → **lagged replica** (`IAuctionReadReplica`), which may return **stale/null** until refreshed.

---

## 6) Event Flow & Failure Modes

### Normal flow (US owner, no partition)

1. `CreateAuction` → write `Auction`; append `AuctionCreated` to `EventStore`; enqueue to `EventOutbox`.
2. `EventPublisher` publishes; EU `DatabaseSyncService` consumes → creates mirror `Auction` in Draft using **VehicleSnapshot**.
3. `Activate` → emit `AuctionActivated`; EU applies → mirror goes Active.
4. `PlaceBid` → write `Bid` (local), append to `EventStore`, enqueue to `Outbox`; EU applies idempotently.

### Partitioned flow

* Link goes **Partitioned**. Both regions continue accepting **local** bids.
* `EventPublisher` still marks Outbox as published, but inter-region channel **buffers**.
* **Heal:** channel flushes buffered events; EU/US `DatabaseSyncService` **applies** and appends to local `EventStore`.
* **Reconcile** at owner: build full set (local + remote), deterministic ordering, cutoff enforcement, winner selection.

### Idempotency & at-least-once

* Each destination stores `AppliedEvent(EventId)` and upserts `Bid` guarded by `UNIQUE(AuctionId, SourceRegionId, Sequence)`.
* Re-delivery is harmless (detected + dropped).

---

## 7) Reconciliation (Deterministic Ordering)

**Deterministic total order:**

1. **Amount DESC**,
2. `CreatedAtUtc ASC`,
3. `SourceRegionId` preference (prefer **Owner** region),
4. `BidId` ASC.

**Algorithm (pseudo-code)**

```csharp
var lastId = checkpoint.Get(auctionId);
var events = eventStore.GetAfter(auctionId, lastId)
                       .OrderBy(e => e.CreatedAtUtc);

foreach (var e in events)
{
    if (e.EventType == "BidPlaced")
    {
        var b = Deserialize<BidPlacedPayload>(e.PayloadJson);
        if (b.CreatedAtUtc <= EndsAtUtc)    // cutoff
            UpsertBidIfNotExists(b);        // UNIQUE(AuctionId, SourceRegionId, Sequence)
    }
}

var winner = GetBids(auctionId)
    .OrderByDescending(b => b.Amount)
    .ThenBy(b => b.CreatedAtUtc)
    .ThenBy(b => b.SourceRegionId == OwnerRegionId ? 0 : 1)
    .ThenBy(b => b.Id)
    .FirstOrDefault();

SetWinner(auctionId, winner?.Id);
checkpoint.Set(auctionId, events.LastOrDefault()?.EventId);
```

---

## 8) Concurrency, Ordering & Duplicate Detection

* **Optimistic concurrency (CAS)** on `Auction` via SQL `rowversion` → avoids lost updates when changing `CurrentHighBid/Seq`.
* **Local monotonic sequences** per `(AuctionId, SourceRegionId)` for `Bid.Sequence` (producer side) guarantee consistent ordering within a region; global order is resolved by the reconciliation rule above.
* **Duplicate detection**: DB constraint **UNIQUE (AuctionId, SourceRegionId, Sequence)** ensures a bid is stored **once** globally.

---

## 9) RegionCoordinator (contract & behaviour)

```csharp
public sealed record PartitionStatus(bool IsPartitioned, DateTime? SinceUtc, IReadOnlyDictionary<string,bool> RegionReachability);

public interface IRegionCoordinator
{
    Task<bool> IsRegionReachableAsync(string region, CancellationToken ct = default);
    Task<PartitionStatus> GetPartitionStatusAsync(CancellationToken ct = default);
    Task<T> ExecuteInRegionAsync<T>(string region, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    event EventHandler<PartitionChangedEventArgs>? PartitionDetected;
    event EventHandler<PartitionChangedEventArgs>? PartitionHealed;
}
```

* **Detected** fired when changing to **Partitioned**; **Healed** when back to **Connected**.
* `ExecuteInRegionAsync<T>` throws if region is currently unreachable.

---

## 10) Testing Strategy (what to look for)

* **Unit tests**

  * Vehicles: region isolation, type validation, soft delete.
  * Read consistency: Strong vs Eventual with **lagged replica** (first read stale).
  * ConflictResolver / ordering tie-breaks.
* **Integration tests (DB)** — **skipped unless** `TEST_SQL_CONNSTR` is set

  * **CAS** via `rowversion` (update succeeds once; retry with old version fails).
  * **UNIQUE** `(AuctionId, SourceRegionId, Sequence)` blocks duplicates.
* **Simulation tests**

  * **Partition → Heal → Reconcile** elects correct winner; **no bids lost**.
  * **Ends during partition**: only bids with `CreatedAtUtc <= EndsAtUtc` are considered valid after heal.

---

## 11) Design Choices & Trade-offs

* **Outbox vs EventStore**: separated to simplify **replay** and retention. Outbox is a **publication queue**; EventStore is a **history log**.
* **No Vehicle replication**: remote mirrors display Vehicle info from **snapshot** in `AuctionCreated`. Avoids cross-region reads and coupling.
* **AP during partitions**: prioritizes user experience (keep bidding). Consistency is restored deterministically on heal.
* **Replica realism**: we simulate a **lagged replica** to make Eventual vs Strong visible in tests; the pattern applies to real replicas.
* **Cutoff enforcement**: ensures fairness when an auction ends while partitioned.

---

## 12) Limitations & Future Work

* Single owner per auction; ownership transfer is out of scope.
* No saga/workflow for cancellation/rollback across regions.
* No retention/compaction policy for EventStore (demonstration only).

---

## 13) How to Verify Locally

```bash
dotnet build
dotnet test                    # all tests

# Optional: only simulation (partition/heal)
dotnet test --filter "FullyQualifiedName~Partition|Category=partition"

# DB integration (requires SQL Server connection string)
# Powershell example:
$env:TEST_SQL_CONNSTR="Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Database=AuctionTestDb"
dotnet test --filter "FullyQualifiedName~DbIntegrationTests"
```

*This document intentionally focuses on the elements requested in the brief: multi-region behavior under partitions, deterministic reconciliation, consistency levels per operation, and the minimal database design and tests to enforce ordering/idempotency and safe concurrency.*
