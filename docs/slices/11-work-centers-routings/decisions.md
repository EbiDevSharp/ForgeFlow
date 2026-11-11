# Slice 11 — Approved Design Decisions (`11-work-centers-routings`)

| Decision ID | Topic | Approved Choice | Rationale |
|---|---|---|---|
| `W-Q01` | Ownership Scope | Site-scoped `WorkCenter` & `Routing` (`OrganizationId, SiteId`) | Equipment/lines belong to specific plants; enables strict composite FK `(OrganizationId, SiteId, WorkCenterId)` without cross-site mismatch. |
| `W-Q02` | Operation Time Units | Minutes (`SetupMinutes`, `RunMinutesPerUnit`, `QueueMinutes`, `WaitMinutes`) | Uniform deterministic capacity & planned duration calculation across `WorkCenter` load queries. |
| `W-Q03` | ProductionOrder Integration | Nullable `ProductionOrder.RoutingId` & `RoutingCodeSnapshot`; snapshot into `ProductionOrderOperation` | Preserves backward compatibility with existing routing-less orders while freezing routing steps upon Draft/Release. |
| `W-Q04` | Shop-Floor Execution State | `Pending (1) -> Ready (2) -> InProgress (3) -> Paused (4) -> Completed (5) / Skipped (6)` | Supports releasing orders (`Pending -> Ready`), pausing/resuming work, reporting setup/run minutes + good/scrap qty, and skipping optional steps. |
| `W-Q05` | Permissions | `PermissionModule.Routings = 15`: `Routings.Read`, `Routings.Manage`, `Operations.Execute` | Separates engineering setup (`Routings.Manage`) from shop-floor operator progress reporting (`Operations.Execute`). |
| `W-Q06` | Navigation Placement | `WorkCenters` (`/work-centers`) & `Routings` (`/routings`) inside `MasterDataNavGroup` after `Boms`; shop-floor execution inside `ProductionOrders` | Aligns engineering setup alongside Units/Items/BOMs while keeping shop-floor operation control directly on Manufacturing Orders. |
