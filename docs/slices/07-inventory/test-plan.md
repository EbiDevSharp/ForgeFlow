# Slice 07 Test & Acceptance Plan (`07-inventory`)

## 1. Unit & Application Tests (`InventoryTests.cs`)
- **I01 — Warehouse validation & default-per-site invariant**: valid `Code` formatting, duplicate `Code` rejection, switching active default warehouse per site.
- **I02 — Append-only ledger & sign rules**: `Receipt > 0`, `Issue < 0`, `Adjustment != 0`, `Transfer` creates paired `-` (source) & `+` (destination) ledger lines with matching `BaseUnitId` quantities.
- **I03 — Hybrid UOM conversion to `BaseUnitId`**: posting receipt in `TON` / `G` or item-specific `BOX` accurately computes `BaseQuantityDelta` in `Item.BaseUnitId` (`KG` / `PCS`), rejecting unconvertible dimensions with stable error codes.
- **I04 — Negative balance protection & concurrency**: attempting an `Issue`, `TransferOut`, or `ProductionIssue` exceeding `OnHandBaseQuantity` fails deterministically with `InventoryErrors.InsufficientStock`; idempotency key deduplicates retries without double-posting.
- **I05 — Production Order issue/receipt rules**: `ProductionIssue` and `ProductionReceipt` succeed only for `Released` or `InProgress` orders in matching `(OrganizationId, SiteId)` and fail for `Draft` / `Cancelled` / `Completed` orders.
- **I06 — Document Reversal**: reversing a posted `InventoryTransaction` posts negated compensating ledger entries + updates `InventoryBalance` atomically without mutating original rows; reversing twice fails cleanly (`AlreadyReversed`).
- **I07 — Site scope isolation & route/permission contracts**: Site-A operator cannot list/post/reverse against Site-B warehouses or balances (`AccessDenied`); `InventoryPermissions` (`Inventory.Read`, `Inventory.Manage`) carry `OrganizationOrSite`; `/inventory` and `/organizations/{OrganizationId:long}/inventory` routes verified; `ResourceContractTests` zero hardcoded text verified.

## 2. Live SQL Server Integration Tests (`InventorySqlTests.cs`)
- **S01 — Schema, append-only runtime permissions & triggers**: verify `[ForgeFlowOrganizationRuntime]` cannot `UPDATE` or `DELETE` `[organization].[InventoryTransaction]` or `[organization].[InventoryLedgerEntry]` and cannot `DELETE` `[organization].[Warehouse]` / `[organization].[InventoryBalance]`; verify SQL `CHECK (OnHandBaseQuantity >= 0)` holds.
- **S02 — Atomic receipt -> issue -> balance -> reversal -> audit**: verify receipt, hybrid conversion, balance calculation, reversal, and site-attributed `OrganizationChange` rows (`Warehouse.*`, `InventoryTransaction.*`).
- **S03 — Concurrent issue barrier test**: two parallel tasks racing to issue `60` units each from a `100`-unit balance under `UPDLOCK, HOLDLOCK`; exactly one succeeds (`40` remaining), second fails with `InsufficientStock`, never going negative.
- **S04 — Production Order integration & Site isolation in SQL**: issue raw materials against a `Released` `ProductionOrder` in Site A, receive finished product into Site A's warehouse, reject Site B cross-site access, and confirm original frozen BOM snapshot remains untouched.

## 3. UI & Bilingual Browser Acceptance (`B01`–`B04`)
- **B01**: `fa-IR` (RTL) + `en-US` (LTR) navigation under `InventoryNavGroup -> /inventory` on `1440px` desktop and `320px` mobile without horizontal overflow.
- **B02**: Create Warehouse -> Post Receipt (`BOX` or `KG`) -> Verify On-Hand balance updated immediately -> Post Production Issue & Receipt -> Reverse document -> Verify movement history & balance restored.
- **B03**: Scope switching (`Organization` vs `Site`) + site-scoped user seeing only authorized site's warehouses & movements.
- **B04**: Verify `BitDataGrid`, `BitDialog`, `DisplayFormatter` numeric/date rendering, and zero console/runtime errors.
