# Constellation – Car Auction (Multi-Region)  
## Architecture & Design Decisions

> This document concisely summarises—aligned with the challenge brief—**the architecture decisions, per‑operation CAP stance, post‑partition reconciliation, data/indices design, and non‑functional assumptions**. Everything described here has a counterpart in the project’s code and tests.

---

## 1) Architecture Overview

- **Multi‑region model** with **per‑auction ownership**. The owner region is the final authority to order bids and elect the winner.
- **During network partitions** (simulated), regions **keep accepting bids** (user‑facing availability — **AP**), storing them with a **local sequence** and source metadata for later **deterministic reconciliation**.
- **Reads** are exposed with **two consistency levels**:
  - **Strong**: query the writer/most recent state (strong consistency, higher latency).
  - **Eventual**: query a **lagged replica** (lower latency, may serve slightly stale data).
- **Reliability/Idempotency**: event outbox + **applied‑event** tracking to tolerate re‑delivery/duplicates.

---

## 2) Consistency Decisions (CP/AP) per Operation

| Operation                          | Target Region       | Consistency | Decision | Notes |
|-----------------------------------|---------------------|-------------|----------|-------|
| **CreateAuction**                 | Auction owner       | **CP**      | Strong   | Created only in the owner to avoid metadata split‑brain. |
| **PlaceBid (intra‑region owner)** | Owner               | **CP**      | Strong   | Ordering via sequence + CAS; single transaction. |
| **PlaceBid (cross‑region)**       | Non‑owner region    | **AP** (partition) | High availability | Accept locally; persist `(AuctionId, SourceRegion, Sequence)` for dedupe and later reconciliation in the owner. |
| **EndAuction**                    | Owner               | **CP**      | Strong   | Sets **EndsAtUtc**. Bids with `CreatedAtUtc > EndsAtUtc` are excluded during reconciliation. |
| **GetAuction (Strong)**           | Writer/Owner        | **CP**      | Strong   | For critical screens (e.g., final auction state). |
| **GetAuction (Eventual)**         | Replica (lagged)    | **AP**      | Scalable | For listings/quick lookups that tolerate stale reads. |

**Rationale:** We maximise availability on user‑latency‑sensitive paths (bid submission), while keeping a **single authority** for the final state (owner), which imposes a **deterministic total order** after the partition heals.

---

## 3) CAP Scenario — “before / during / after”

### 3.1 Before (healthy network)
- Bids are accepted in the **owner**, with a **monotonic sequence** and `CurrentHighBid` updated under **CAS (`rowversion`)** within a single transaction.
- Outbox records events for replication/observability.

### 3.2 During partition (e.g., 5 minutes)
- **Each region continues accepting bids** locally (**AP**).  
- The non‑owner assigns a **local `Sequence`** and stores source metadata **`(AuctionId, SourceRegion, Sequence)`** (unique key for dedupe).  
- **EndAuction** is recorded in the **owner** with `EndsAtUtc`.  
- **Never** select a winner during a partition (only mark end).

### 3.3 After the partition heals
- The **owner** gathers all bids (local + remote) with `CreatedAtUtc ≤ EndsAtUtc` and applies a **deterministic total order**.  
- It updates `WinnerBidId`/auction state and publishes a reconciliation checkpoint.

**Pseudocode (summary):**
```csharp
Bid ResolveWinner(IEnumerable<Bid> allBids, DateTime endsAtUtc)
{
    var eligible = allBids.Where(b => b.CreatedAtUtc <= endsAtUtc);
    // Deterministic ordering:
    // 1) Highest Amount
    // 2) Earliest CreatedAtUtc
    // 3) Stable tie-break: OwnerFirst, then SourceRegion, then Sequence
    var ordered = eligible
        .OrderByDescending(b => b.Amount)
        .ThenBy(b => b.CreatedAtUtc)
        .ThenBy(b => b.SourceIsOwner ? 0 : 1)
        .ThenBy(b => b.SourceRegion)
        .ThenBy(b => b.Sequence);
    return ordered.FirstOrDefault();
}
```

---

## 4) Conflict Resolution Rules

- **Equal amounts**: tie‑break by `CreatedAtUtc` (earlier wins); persisting ties → prioritise `SourceIsOwner`, then `SourceRegion`, then `Sequence`.  
- **Bids after end** (`CreatedAtUtc > EndsAtUtc`): **excluded**.  
- **Duplicates/re‑delivery**: **UNIQUE(AuctionId, SourceRegion, Sequence)** + **AppliedEvent** table; any reprocessing is **idempotent**.  
- **Hot‑path concurrency**: update `CurrentHighBid/CurrentSeq` with **`rowversion`** and **bounded retry** (e.g., light exponential backoff).

---

## 5) Transaction Boundaries & Isolation

**PlaceBid (owner):** single transaction containing:  
1. Fetch next valid `Sequence` (monotonic per auction).  
2. Validate order and value against `CurrentHighBid`.  
3. Insert `Bid`.  
4. Update `Auction (CurrentHighBid, CurrentSeq, RowVersion)` via **CAS**.  
5. Record event in the **Outbox**.  

**Reconcile/End:**  
- **EndAuction** fixes `EndsAtUtc` (CP).  
- **Reconcile** (post‑heal): read eligible set, decide the winner, update `WinnerBidId` + checkpoint in a short transaction.

**Isolation:** `READ COMMITTED` + CAS with `rowversion` is sufficient (no need for `SERIALIZABLE`) because ordering rules are enforced by sequence and `rowversion` prevents lost updates.

---

## 6) Data Model & Indices

### Core Tables
- **Region**(Id, Name, …)  
- **Vehicle**(Id, Type, …) – **not replicated** cross‑region (regional scope).  
- **Auction**(Id, VehicleId, OwnerRegionId, State, EndsAtUtc, CurrentHighBid, CurrentSeq, WinnerBidId, RowVersion)  
- **Bid**(Id, AuctionId, Amount, BidderId, CreatedAtUtc, SourceRegion, Sequence, SourceIsOwner, …)  
- **OutboxEvent**(Id, AggregateId, Type, Payload, CreatedAtUtc, ProcessedAtUtc, …)  
- **AppliedEvent**(EventId, AppliedAtUtc, …) – idempotency.

### Recommended Indices
- `IX_Auction_Owner_State (OwnerRegionId, State) INCLUDE (EndsAtUtc, CurrentHighBid, CurrentSeq)` – owner/state listings.  
- `IX_Auction_Ends (EndsAtUtc)` – scanning approaching ends.  
- `IX_Bid_Auction_Created (AuctionId, CreatedAtUtc)` – history/leaderboard.  
- `UQ_Bid_Dedupe (AuctionId, SourceRegion, Sequence)` – **cross‑region dedupe**.  
- `PK`s on `Id` + `RowVersion` (timestamp/rowversion) in **Auction** for CAS.

---

## 7) Non-Functional Requirements (Assumptions) & Limitations

- **Latency target**: **p95 < 200 ms** for intra‑region `PlaceBid` and **< 300 ms** for `Strong` reads on the writer; `Eventual` tends to be faster.  
- **Reference scale**: ~**10k active users** and **1k auctions** per region; DB connection pool sized appropriately; read‑heavy paths go to the **replica**.  
- **Availability target**: **99.9% per region** (in production: redundant writers, durable bus, alerting).  
- **Exercise constraints**: “network” and replication are **simulated** (no real broker); focus is **model + deterministic reconciliation**, not UI/auth.

---

## 8) How to Run (essentials)

**Build & general tests:**
```bash
dotnet build
dotnet test
```

**Partition simulations (tagged tests):**
```bash
dotnet test --filter "FullyQualifiedName~Partition|Category=partition"
```

**SQL integration tests** (optional): set `TEST_SQL_CONNSTR` to LocalDB/SQL; if unset, these tests are skipped.
