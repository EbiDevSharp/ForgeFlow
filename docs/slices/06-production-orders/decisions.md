# Slice 06 — Design Decisions (`06-production-orders`)

Status: **PROPOSED (`06.0`)** — Confirm `Q-P01`–`Q-P04` before `06.1` implementation.

## `Q-P01` — Site-Scoped Ownership & Delegated Permissions
- **Question:** Should `ProductionOrder` carry both `OrganizationId` and mandatory `SiteId` (referencing `organization.Site (OrganizationId, Id)`), with both `ProductionOrders.Read` and `ProductionOrders.Manage` marked `PermissionScopeApplicability.OrganizationOrSite`?
- **Recommended option:** **Yes (`Site`-owned, `OrganizationOrSite` permissions).**
  - An `OrganizationAdministrator` (`RoleId = 1`) holds both permissions at Organization scope and every Site in that organization.
  - A `SiteOperator` or planner granted `ProductionOrders.Manage` at `Site A` can create, release, start, and complete Production Orders **only for `Site A`** (never Site B), while reading organization-shared `Items` and `Boms` using their `.Read` grants.
  - Matches `ForgeFlow.md` `FF-A018` ("site A access never implies site B access") and Slice 01 Ownership Matrix (`Q1`).

## `Q-P02` — BOM Resolution Timing, `PLANT` Injection, and Snapshot Immutability
- **Question:** When is the BOM resolved and frozen onto a `ProductionOrder`?
- **Recommended option:** **Preview/Resolve on Draft creation & refresh; permanently freeze on `Released`:**
  1. When creating (or updating) a `Draft` Production Order (`ItemId`, `RequestedQuantity`, `RequestedUnitId`, `PlannedStartDate`, `ContextPairs`), ForgeFlow builds the effective `ManufacturingContext`—automatically injecting `PLANT = Site.Code` if the user did not supply an explicit `PLANT` override—and executes the Slice 05 `BomUseCases` resolver + **1-level or multi-level selectable BOM snapshot** (default: direct resolved BOM inputs/outputs + resolved sub-assembly BOM version metadata for traceability).
  2. Planner can also click **"Refresh BOM Resolution"** while the order is in `Draft` if master data was just corrected.
  3. On transition **`Draft -> Released`**, ForgeFlow re-validates that the Item, Site, Units, and resolved `BomVersion` (`Active`) are still valid, freezes `BomId`, `BomVersionId`, `BomVersionNumber`, `BaseOutputQuantity`, `BaseOutputUnitId`, `ProductionOrderContext`, `ProductionOrderInput`, and `ProductionOrderOutput`, and sets `ReleasedAtUtc`.
  4. Once `Status != Draft`, snapshot fields, BOM references, and planned lines are **structurally immutable** (`ProductionOrders.OrderImmutable`), even if a planner later supersedes or obsoletes that `BomVersion`!

## `Q-P03` — Lean Execution Actuals Boundary (No Warehouse Stock Ledger Yet)
- **Question:** Should Slice 06 record actual reported output/scrap on the `ProductionOrder` without building Inventory Stock Balances / Warehouses yet?
- **Recommended option:** **Yes (Operational execution fields on `ProductionOrder` only).**
  - Add `ActualProducedQuantity` (`decimal(18,6)` nullable / default `0`) and `ActualScrapQuantity` (`decimal(18,6)` default `0`), plus lifecycle timestamps (`ReleasedAtUtc`, `StartedAtUtc`, `CompletedAtUtc`, `CancelledAtUtc`) and `CancellationReason`.
  - Keep actual warehouse stock deduction, lot numbers, and stock receipts strictly out-of-scope for Slice 06 so Slice 07 (**Inventory & Warehouses**) can consume `ProductionOrder` without coupling or schema rewrites.

## `Q-P04` — Navigation Group & Calendar Dates
- **Question:** Where should `/production-orders` live in `MainLayout.razor`, and how should order dates be modeled?
- **Recommended option:**
  - Add a new collapsible **Manufacturing / Production (`ProductionNavGroup`)** section in `MainLayout.razor` (hidden completely if the signed-in actor has no `ProductionOrders.Read` scope), keeping **Master Data & Engineering** (`Units`, `Items`, `BOMs`) cleanly separated from **Production Execution** (`Production Orders`).
  - Store `PlannedStartDate` and `PlannedDueDate` as `DateOnly` (matching `BomVersion.EffectiveFromDate`/`EffectiveToDate` so BOM resolution at `PlannedStartDate` has zero timezone drift) and present them with culture-aware `BitDatePicker` (`fa-IR` Jalali / `en-US` Gregorian). Store `ReleasedAtUtc`, `StartedAtUtc`, `CompletedAtUtc`, `CancelledAtUtc` as UTC `datetime2(7)`.
