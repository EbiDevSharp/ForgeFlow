# Slice 07 — Warehouses, Stock Ledger & Production Material Issue/Receipt (`07-inventory`)

Status: **DESIGN PROPOSED (`07.0`)** (2026-11-10) — Implements the **Warehouse & Inventory Ledger domain** per [ForgeFlow.md](../../../ForgeFlow.md) row `انبار/بچ و lot` and `Inventory = What is actually available`: tracking physical warehouse balances per `(OrganizationId, SiteId, WarehouseId, ItemId, LotCode)` via immutable append-only ledger entries, hybrid item+global UOM conversion to each item's `BaseUnitId`, and explicit material issue / finished-goods receipt links to `ProductionOrder`s without ever mutating historical BOMs or order snapshots.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
- Follows **Slice 06 (`06-production-orders` — ACCEPTED)** and **Slice 05 (`05-item-catalog` — ACCEPTED)**.
- Enforces the architectural separation in `ForgeFlow.md`:
  - `BOM`: what is **planned** (`BomVersion`)
  - `ProductionOrder`: what is **committed** (`ProductionOrder` + frozen BOM snapshot)
  - `Inventory`: what is **actually available and moved** (`Warehouse`, `InventoryBalance`, append-only `InventoryLedgerEntry`)

### Actors and trusted provenance
- **Organization administrator (`RoleId = 1`)**: granted `Inventory.Read` and `Inventory.Manage` across the organization and all active sites.
- **Site-scoped inventory / production operator**: granted `Inventory.Read` and/or `Inventory.Manage` at a specific `Site(OrganizationId, SiteId)` scope via `/roles` or `/users`. A grant at Site A never views or mutates Warehouses, Stock Balances, or Ledger documents belonging to Site B.
- **Provenance**: every read and write resolves the caller through `ITrustedActorAccessor` and authorizes exact `Organization` or `Site` scope via `IOrganizationPermissionEvaluator`; every state mutation writes atomically with site-scoped `organization.OrganizationChange` records.

### Preconditions
- Accepted **Slice 05** (`Unit`, hybrid `UnitConversion` / `ItemUnitConversion`, `Item`) and **Slice 06** (`ProductionOrder` lifecycle `Draft / Released / InProgress / Completed / Cancelled`).

### Capabilities & Outputs
- **Warehouses (`organization.Warehouse`)**: site-scoped stock locations `(OrganizationId, SiteId, Code, Name, IsDefaultForSite, IsActive)`.
- **Append-only Movement Documents (`InventoryTransaction` + `InventoryLedgerEntry`)**:
  - `Receipt (1)` — Manual / Opening / Purchase Receipt (`+`)
  - `Issue (2)` — General / Scrap / Ad-hoc Issue (`-`)
  - `Adjustment (3)` — Signed Inventory Adjustment (`+` or `-`)
  - `TransferOut (4)` / `TransferIn (5)` — Atomic two-legged inter-warehouse transfer
  - `ProductionIssue (6)` — Material consumption posted against a `Released` or `InProgress` `ProductionOrder` (`-`)
  - `ProductionReceipt (7)` — Finished-good / co-product / by-product receipt posted against a `Released` or `InProgress` `ProductionOrder` (`+`)
  - `Reversal (8)` — Compensating reversal of an existing posted `InventoryTransaction` (preserving append-only ledger history; original rows are never updated or deleted).
- **Materialized On-Hand Balance (`organization.InventoryBalance`)**:
  - Updated atomically under SQL `UPDLOCK, HOLDLOCK` row/range lock on `(OrganizationId, WarehouseId, ItemId, LotCode)` inside the same transaction as `InventoryLedgerEntry`.
  - Quantity normalization: user enters `(EnteredQuantity, EnteredUnitId)` and the server converts using Slice 05's exact hybrid item+global UOM graph into `Item.BaseUnitId` (`BaseQuantityDelta`).
- **UI (`/inventory` and `/organizations/{OrganizationId:long}/inventory`)**:
  - Tabbed `Bit.BlazorUI 10.7.0` workspace: **On-Hand Balances**, **Stock Movements / Ledger**, **Warehouses**, plus **Post Movement / Issue / Receipt / Transfer** and **Reverse Document** dialogs using `BitDataGrid`, `BitDialog` (`ShowOkButton="false" ShowCancelButton="false"`), and full `fa-IR` / `en-US` localization.

### Out-of-scope (deferred to later slices)
- **FIFO/LIFO Costing Layers, Standard vs Actual Cost Variance, and Accounting GL Journal Posting** (Costing/Finance slices).
- **Serial-number-per-unit scanning, Bin/Rack sub-locations, Quality Hold / Inspection quarantine workflows, Cycle-count approval sheets, and Procurement/Sales Order fulfillment** (deferred to dedicated Operations / Quality / Supply-Chain slices).

## 2. Operation x Permission x Scope matrix

| Permission code | Scope applicability | Covered operations |
|---|---|---|
| `Inventory.Read` | `OrganizationOrSite` | List/filter Warehouses, view serialized On-Hand Balances by item/warehouse/lot, browse append-only `InventoryTransaction` & `InventoryLedgerEntry` history within authorized org/site |
| `Inventory.Manage` | `OrganizationOrSite` | Create/update/activate/deactivate Warehouses, post `Receipt`, `Issue`, `Adjustment`, `Transfer`, `ProductionIssue`, `ProductionReceipt`, and `ReverseTransaction` |

## 3. UI & Localization Standards
- `BitDataGrid` server-side paging (`25`, `10/25/50/100`), no `Class` on `<BitDataGridColumn>`.
- `BitDialog` (`ShowOkButton="false"`, `ShowCancelButton="false"`, `ShowCloseButton="true"`) with `.po-form-grid` 2-column alignment and tinted `.po-dialog-footer`.
- Codes (`Warehouse.Code`, `DocumentNumber`, `Item.Code`, `LotCode`) in `<bdi dir="ltr">`, quantities via `DisplayFormatter.Number` (`"N2"` or precision-aware formatting), dates via `DisplayFormatter.Date` / `BitDatePicker`, and zero hardcoded UI text (`fa-IR` / `en-US` `.resx` + `ResourceContractTests`).

## 4. Next step
- Confirm open design decisions `Q-I01`–`Q-I04` in [decisions.md](decisions.md) before starting `07.1` (Domain & Application).
