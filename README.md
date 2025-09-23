# Constellation Auction Challenge

## How to run

- `dotnet build`
- `dotnet test` (unit, integration, simulation)

## Docs
- Architecture: `/docs/architecture/Architecture.md`
- ERD: `/docs/erd/ERD.md`
- Design decisions: `/docs/decisions/DesignDecisions.md`

## Notes
- Strong reads (Write DB) vs Eventual (Read Replica)
- Partition simulation in `Auction.SimulationTests`
