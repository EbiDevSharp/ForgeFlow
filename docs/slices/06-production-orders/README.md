# Slice 06 — Production Orders & Immutable BOM Snapshot (`06-production-orders`)

Status: **DESIGN PROPOSED (`06.0`)** (2026-11-10) — Establishes the **Production Order / Manufacturing Order boundary (`Day 11`)** per [ForgeFlow.md](../../../ForgeFlow.md) `Next Step — Day 11` and Section `7. Historical Traceability`: converting a resolved `BomVersion` and multi-level/single-level requirement into a committed, site-scoped production order whose BOM reference (`BomId`, `BomVersionId`, `VersionNumber`), effective context criteria, and planned input/output snapshot cannot be rewritten by subsequent master-data changes.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
- Follows directly after **Slice 05 (`05-item-catalog` — ACCEPTED)** and implements the Day 11 architectural boundary in [ForgeFlow.md](../../../ForgeFlow.md#L3443-L3445):
  - When a manufacturing demand becomes a committed `ProductionOrder`, ForgeFlow resolves the effective `BomVersion` (via `BomResolutionEngine` / `BomUseCases.ExplodeBom` using the order's `Site`, `PlannedStartDate`, `RequestedQuantity`, `RequestedUnit`, and `ManufacturingContext`) and **persists an immutable BOM snapshot** (`ProductionOrder` header + `ProductionOrderInput` + `ProductionOrderOutput` + `ProductionOrderContext`).
  - Later edits, activations, supersedences, or obsoletions on `Bom` / `BomVersion` **never mutate existing released or historical production orders**.

### Actors and trusted provenance
- **Organization administrator (`RoleId = 1`)**: automatically granted `ProductionOrders.Read` and `ProductionOrders.Manage` across the organization and all active sites.
- **Site-scoped production planner / operator**: can be granted `ProductionOrders.Read` and/or `ProductionOrders.Manage` at a specific `Site(OrganizationId, SiteId)` scope via `/roles` or `/users`. A grant at Site A never exposes or modifies Production Orders belonging to Site B.
- **Provenance**: every read and write resolves the caller via `ITrustedActorAccessor` and authorizes exact `Organization` or `Site` scope via `IOrganizationPermissionEvaluator` (`ProductionOrder` writes are atomic with `organization.OrganizationChange`).

### Preconditions
- Accepted **Slice 05** (`Units`, `Items`, `Boms`, `BomVersions`, `ContextDimensions`) and Slices `01`–`04` (`Organization`, `Site`, `Users`, `Roles`, `OrganizationChange` audit).

### Lifecycle & Outputs
- `ProductionOrderStatus`:
  - `Draft (1)` → initial editable state; planner can adjust planned quantity, dates, context criteria, notes, or re-run BOM resolution preview before release.
  - `Released (2)` → freezes the resolved `BomId`, `BomVersionId`, `VersionNumber`, `ContextSnapshotJson`, `ProductionOrderInput` rows, and `ProductionOrderOutput` rows as immutable snapshot records.
  - `InProgress (3)` → manufacturing execution underway; operator may record actual produced/scrap quantities (`ActualProducedQuantity`, `ActualInputConsumed` summary fields per `Q-P03`).
  - `Completed (4)` → immutable final manufacturing record with actuals and `CompletedAtUtc`.
  - `Cancelled (5)` → terminal cancellation state (allowed from `Draft`, `Released`, or `InProgress` with reason note); immutable thereafter.
- **Outputs**:
  - `PermissionModule.ProductionOrders = 9`: `ProductionOrders.Read`, `ProductionOrders.Manage` (`OrganizationOrSite`).
  - `organization.ProductionOrder`, `organization.ProductionOrderInput`, `organization.ProductionOrderOutput`, `organization.ProductionOrderContext`.
  - Secured `ProductionOrderUseCases`, `SqlProductionOrderOperationsStore`, `/production-orders` and `/organizations/{OrganizationId:long}/production-orders` UI using `BitDataGrid`, `BitDialog` (no OK/Cancel footer buttons), and culture-aware `BitDatePicker` (`fa-IR` Jalali / `en-US` Gregorian).

### Out-of-scope (deferred to later slices)
- **Warehouse / Stock Ledger & Material Issue/Receipt transactions** (Slice 07 — Inventory).
- **Shop-floor Routing, Work Centers, Machine Scheduling, Labor tracking, Quality/Lot traceability, and Standard/Actual Costing** (future Operations/Costing slices).

## 2. Operation x Permission x Scope matrix

| Permission code | Scope applicability | Covered operations |
|---|---|---|
| `ProductionOrders.Read` | `OrganizationOrSite` | List/search Production Orders, view snapshot inputs/outputs/context, inspect execution history within authorized org/site |
| `ProductionOrders.Manage` | `OrganizationOrSite` | Create `Draft` Production Order, edit/refresh draft, release (`Draft -> Released`), start (`Released -> InProgress`), record execution actuals, complete (`InProgress -> Completed`), cancel (`-> Cancelled`) |

## 3. UI & Localization Standards
- Identical `Bit.BlazorUI 10.7.0` standards as Slice 05 (`BitDataGrid` server-side paging `10/25/50/100`, `BitDialog` `ShowOkButton="false"` `ShowCancelButton="false"`, `BitDatePicker` Jalali in `fa-IR` and Gregorian in `en-US`, zero hardcoded display text, `fa-IR`/`en-US` `.resx` catalogs, `ResourceContractTests` verified).
- Order/item/BOM codes use `<bdi dir="ltr">`, dates use `DisplayFormatter.Date` / `BitDatePicker`, and context dimensions reuse `RolePermissionLocalization.ContextDimensionDisplayName`.

## 4. Next step
- Confirm open design decisions `Q-P01`–`Q-P04` in [decisions.md](decisions.md) before starting `06.1` (Domain & Application).
