# Slice 10 — Suppliers & Purchase Orders (`10-procurement`)

## 1. Scope & Business Objective
Deliver Organization-shared **Supplier Management** and Site-scoped **Purchase Order (PO)** lifecycle management with atomic **Goods Receipt** posting into ForgeFlow's append-only Inventory Ledger (Slice 07), ItemLot & Quality Control quarantine pipeline (Slice 09), and Operational Material Shortage visibility (Slice 08):

- **Organization-Shared Suppliers (`Supplier`)**:
  - Unique `Code` per Organization (`[A-Z0-9_-]`, max 32 chars, reserved when inactive), `Name`, optional `ContactName`, `Phone`, `Email`, `Address`, `Notes`, `IsActive`, `RowVersion`, audit metadata.
  - Inactive suppliers cannot be selected on new/edited/approved POs.
- **Site-Scoped Purchase Orders (`PurchaseOrder` + `PurchaseOrderLine`)**:
  - Lifecycle: `Draft (1)` → `Approved (2)` → `PartiallyReceived (3)` → `Received (4)`, plus `Cancelled (5)` (only from `Draft`/`Approved` when zero receipts exist) and `Closed (6)` (short-close remaining from `PartiallyReceived`).
  - Drafts support creating, editing header & lines (`Item.CanBePurchased == true`, `OrderedQuantity > 0`, unit convertible to Item's `BaseUnitId` via `UnitConversionGraph`, optional `UnitPrice >= 0` + `CurrencyCode`), and approving.
  - After `Approved`, header & ordered line structure are frozen; only receiving (`ApplyReceipt`), short-close (`CloseRemaining`), or cancellation (`Cancel` while `Approved` with zero receipts) are permitted.
- **Atomic Goods Receipt (`ReceivePurchaseOrder`)**:
  - Validates same-site active `Warehouse`, `OrderedBaseQuantity - ReceivedBaseQuantity >= incomingBase` (strict no-over-receipt), lot-tracking rules (`Item.RequiresLotTracking`), `ItemLot` manufacture/expiration dates, and `IdempotencyKey` / `DocumentNumber`.
  - In ONE serializable SQL transaction: creates `InventoryTransaction` (`TransactionType = Receipt`, `PurchaseOrderId = po.Id`) + `InventoryLedgerEntry`, updates `InventoryBalance` (`OnHandBaseQuantity` & `BlockedBaseQuantity`), creates/updates `ItemLot`, creates `Pending` `QualityInspection` (`SourceType = Receipt`) when `QualityControlMode` is `IncomingReceipts` or `AllReceipts`, increments `PurchaseOrderLine.ReceivedBaseQuantity`, and transitions `PurchaseOrder.Status` (`PartiallyReceived` or `Received`).
  - Reversing a PO `Receipt` transaction via `/inventory` atomically reverts `PurchaseOrderLine.ReceivedBaseQuantity` and recalculates `PurchaseOrder.Status` (`Received`/`PartiallyReceived` → `PartiallyReceived`/`Approved`).
- **Operational Reporting & Navigation**:
  - Material Shortage (`/reports`) adds informational `OnOrderBaseQuantity` (`SUM(OrderedBaseQuantity - ReceivedBaseQuantity)` across `Approved`/`PartiallyReceived` POs in scope) without changing physical shortage (`max(0, Required - Available)`).
  - Dedicated `ProcurementNavGroup` (`/suppliers`, `/purchase-orders`) positioned between `MasterDataNavGroup` and `InventoryNavGroup`.

## 2. Increment Plan
- `10.0`: Design contracts (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`)
- `10.1`: Domain (`PermissionCatalog`, `InventoryTransaction`, `ForgeFlow.Domain.Procurement`)
- `10.2`: Application (`ProcurementContracts`, `ProcurementUseCases`, updates to `AuditEventCatalog`, `DiagnosticContracts`, `InventoryContracts`, `ReportingContracts`, `ReportingUseCases`)
- `10.3`: Infrastructure (`OrganizationModel`, `OrganizationDbContext`, `SqlProcurementOperationsStore`, updates to `SqlInventoryOperationsStore`, `SqlReportingQueryStore`, `OrganizationPersistenceRegistration`, EF migration + `upgrade-local-testdb.ps1`)
- `10.4`: Web & Localization (`ReadScopes`, `MainLayout.razor`, `Suppliers.razor`, `PurchaseOrders.razor`, updates to `Inventory.razor`, `Reports.razor`, `RolePermissionLocalization.cs`, `fa-IR`/`en-US` `.resx`, `app.css`)
- `10.5`: Verification & Acceptance (`ProcurementTests`, `ProcurementSqlTests`, updated model/role/resource/scanner tests, DB upgrade, Release build, live SQL & browser checks)
