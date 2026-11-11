# Slice 09 — Quality Control, Quarantine Holds & Multi-Level Lot Traceability (`09-quality-traceability`)

## 1. Scope & Business Objective
Deliver factory-grade Quality Control (QC), stock quarantine blocking, partial inspection dispositions, lot expiration control, and multi-level forward/backward lot genealogy across `Organization` and `Site` boundaries:
- **Item Quality & Lot Policy (`Item`)**:
  - `RequiresLotTracking`: enforces mandatory `LotCode` on inbound/outbound stock movements for that item.
  - `QualityControlMode`: `None (0)`, `IncomingReceipts (1)`, `ProductionReceipts (2)`, `AllReceipts (3)` to auto-create pending QC inspections and block newly received stock until QC releases it.
- **Non-Negative Blocked Stock (`InventoryBalance`)**:
  - `BlockedBaseQuantity` tracks quantities held under Pending QC / Quarantine (`0 <= BlockedBaseQuantity <= OnHandBaseQuantity`).
  - `AvailableBaseQuantity = OnHandBaseQuantity - BlockedBaseQuantity` is enforced on `Issue`, `ProductionIssue`, and `TransferOut` under `UPDLOCK, HOLDLOCK`.
- **QC Inspection Lifecycle & Partial Disposition (`QualityInspection` + `QualityInspectionLine`)**:
  - Supports automatic creation from inbound `Receipt` / `ProductionReceipt` and manual ad-hoc QC Holds on existing warehouse stock.
  - Optional lab/measurement lines (`ParameterName`, `ExpectedSpec`, `MeasuredValue`, `IsPassed`, `Notes`) and `DefectReasonCode`.
  - Partial or full disposition:
    - `AcceptedBaseQuantity` releases `BlockedBaseQuantity` back to available stock.
    - `RejectedBaseQuantity` posts an automatic compensating negative `InventoryTransaction` (`QualityScrap = 9`, linked via `QualityInspectionId`) reducing both `OnHandBaseQuantity` and `BlockedBaseQuantity`.
    - Remaining quantity stays on Quarantine (`BlockedBaseQuantity`) for follow-up inspection/rework.
- **Lot Dates, Expiry Blocking & Multi-Level Genealogy (`ItemLot` + `InventoryLedgerEntry`)**:
  - Optional `ManufactureDate`, `ExpirationDate`, and `Notes` per `(OrganizationId, ItemId, LotCode)`.
  - Outbound `Issue`, `ProductionIssue`, and `TransferOut` strictly reject expired lots (`ExpirationDate < Today`) unless QC updates/extends the lot expiration date or scraps the stock.
  - Cycle-safe recursive Forward (`Raw Lot -> ProductionOrders -> Finished Lots -> ...`) and Backward (`Finished Lot -> ProductionOrders -> Consumed Input Lots -> ...`) genealogy computed from immutable `InventoryLedgerEntry` facts (netting `Reversal` transactions) combined with QC inspection history.
- **Reporting & Operational Integration**:
  - Slice 08 Material Shortage (`/reports`) uses `AvailableBaseQuantity` (`OnHand - Blocked`) as usable supply and displays `BlockedBaseQuantity` separately.
  - Dedicated `QualityNavGroup` in `MainLayout.razor` linking to `/quality` and `/traceability`, plus deep links from `/inventory` and `/production-orders`.

## 2. Out of Scope (Deferred to Slices 10–12)
- Suppliers & Purchase Orders (`Slice 10`)
- Work Centers, Routings & Shop-Floor Operations (`Slice 11`)
- Soft/Hard Stock Reservation (`AllocatedBaseQuantity`), MRP netting, and Executive Dashboard (`Slice 12`)
- Full QMS AQL sampling tables & calibration schedules

## 3. Increment Plan
- `09.0`: Design contracts (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`)
- `09.1`: Domain (`PermissionCatalog`, `Item`, `InventoryBalance`, `InventoryTransaction`, `ForgeFlow.Domain.Quality`)
- `09.2`: Application (`QualityUseCases`, `QualityContracts`, updates to `ItemUseCases`, `InventoryUseCases`, `ReportingUseCases`, `UserOperationCatalog`, `AuditEventCatalog`)
- `09.3`: Infrastructure (`OrganizationModel`, `OrganizationDbContext`, `SqlQualityOperationsStore`, `SqlInventoryOperationsStore`, `SqlReportingQueryStore`, `ItemOperationsStore`, `RuntimeSessions`, EF migration + `upgrade-local-testdb.ps1`)
- `09.4`: Web & Localization (`Quality.razor`, `Traceability.razor`, updates to `Items.razor`, `Inventory.razor`, `ProductionOrders.razor`, `Reports.razor`, `Audit.razor`, `MainLayout.razor`, `ReadScopes.cs`, `RolePermissionLocalization.cs`, `fa-IR`/`en-US` `.resx`)
- `09.5`: Verification & Acceptance (`QualityTests`, `QualitySqlTests`, updated `ReportTests`/`ReportSqlTests`/`InventoryTests`, `ResourceContractTests`, Playwright `fa-IR`/`en-US` RTL/LTR desktop + `320px` mobile checks, live user review)
