# Slice 09 Test Plan (`09-quality-traceability`)

## 1. Domain & Application Unit Tests (`QualityTests`)
- `Q01`: `Item` validation for `RequiresLotTracking` and `QualityControlMode` (`None..AllReceipts`), `InventoryBalance` `BlockQuantity` / `ReleaseBlockedQuantity` / `ApplyBlockedDelta` enforcing `0 <= BlockedBaseQuantity <= OnHandBaseQuantity` and `AvailableBaseQuantity`.
- `Q02`: `QualityInspection` domain invariants: positive `InspectedBaseQuantity`, partial disposition sum validation (`Accepted + Rejected <= Remaining`), terminal status transitions (`Completed`/`Cancelled` immutable), and `ItemLot` date bounds (`ExpirationDate >= ManufactureDate`).
- `Q03`: `InventoryUseCases` integration: mandatory `LotCode` when `Item.RequiresLotTracking == true`, auto-creation of `Pending` `QualityInspection` + `BlockedBaseQuantity` on matching `Receipt` / `ProductionReceipt`, blocking outbound `Issue`/`ProductionIssue`/`TransferOut` against `BlockedBaseQuantity`, and blocking expired lots (`ExpirationDate < today`).
- `Q04`: `QualityUseCases` authorization (`Quality.Read` vs `Quality.Manage`, site isolation, warehouse operator without `Quality.Manage` denied release/disposition) and `UserOperationCatalog` rejected operation audit capture.
- `Q05`: `ReportingUseCases` Material Shortage uses `AvailableBaseQuantity` (`OnHand - Blocked`) and surfaces `BlockedBaseQuantity`.
- `Q06`: Multi-level recursive Forward & Backward Lot Genealogy (raw lot -> sub-assembly PO -> WIP lot -> FG PO -> FG lot, excluding reversed transactions, cycle-safe).

## 2. Live SQL Server Integration Tests (`QualitySqlTests`)
- `S01_S04`: End-to-end SQL test with two sites (`Site A`, `Site B`):
  - Lot-tracked + QC-enabled raw item & FG item.
  - Inbound receipt places stock into `BlockedBaseQuantity` and creates `QualityInspection`.
  - Outbound `ProductionIssue` fails with `InsufficientStock` while blocked, then succeeds after partial QC `Accept` + `QualityScrap` (verifying `QualityScrap` append-only `InventoryLedgerEntry` + `OnHandBaseQuantity` / `BlockedBaseQuantity` consistency).
  - Expired `ItemLot` blocks `Issue`, succeeds after QC updates `ExpirationDate`.
  - Multi-level Forward/Backward genealogy returns exact lineage and QC records; `Site B` cannot inspect or view `Site A` records; `[ForgeFlowOrganizationRuntime]` cannot `DELETE` `QualityInspection` or `ItemLot`.
- Update `ReportSqlTests` to confirm `BlockedBaseQuantity` reduces `AvailableBaseQuantity` in Material Shortage.

## 3. Localization & UI Acceptance
- `ResourceContractTests` passes for all `Quality.*`, `Inventory.*`, `Items.*`, `Reporting.*`, and audit event keys in `fa-IR` & `en-US`.
- Playwright + manual live browser acceptance on `/quality`, `/traceability`, `/items`, `/inventory`, `/reports` in `fa-IR` (RTL) and `en-US` (LTR) at `1440px` and `320px` mobile with zero horizontal overflow and zero console errors.
