# Slice 08 — Material Shortage & Production Consumption / Yield Variance Reports (`08-reports-variance`)

Status: **IMPLEMENTED & PUSHED (`08.1`–`08.4`, User Requested Commit/Push; live SQL tests optional locally)** (2026-11-11) — Implements operational analytics and safe CSV export per [ForgeFlow.md](../../../ForgeFlow.md) rows `گزارش‌های پایه` and `تولید واقعی/کیفیت`:
1. **Material Shortage / Net Requirements Report**: compares open production demand (`Released` and `InProgress` `ProductionOrder` non-sub-assembly input requirements normalized via Slice 05 hybrid UOM conversion to each item's `BaseUnitId`) against real physical `InventoryBalance` on-hand quantities across active warehouses in the authorized site(s).
2. **Production Consumption & Yield Variance Report**: compares frozen `ProductionOrder` BOM input/output commitments (`ProductionOrderInput`, `ProductionOrderOutput`, `BaseOutputQuantitySnapshot`) against net posted `InventoryLedgerEntry` movements (`ProductionIssue` net of `Reversal` and `ProductionReceipt` net of `Reversal`) and recorded actual/scrap order outputs (`ActualProducedQuantity`, `ActualScrapQuantity`), including drill-down per order.
3. **Authorized Safe CSV Export**: UTF-8 with BOM (`\uFEFF`) RFC-4180 export with formula-injection neutralization for both reports under explicit `Reports.Export` authorization.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Architectural boundary
- **Zero schema changes**: reuses existing `OrganizationDbContext` (`27` entities, `6` migrations) and `AuthDbContext` (`RolePermission` stores permission strings dynamically).
- **Read-only analytics (`AsNoTracking`)**: reporting never mutates `ProductionOrder`, `InventoryBalance`, or `InventoryLedgerEntry`.
- **Site isolation**:
  - `Reports.Read` (`OrganizationOrSite`) gates viewing shortage and variance reports and order variance drill-downs.
  - `Reports.Export` (`OrganizationOrSite`) gates downloading CSV exports.
  - A site-scoped user granted Site A only sees demand, stock balances, and production orders for Site A; Site B data never leaks into totals or rows.
  - Organization-scoped users (`Organization` scope or all active sites) may inspect across sites or filter to a specific `SiteId`.

### Preconditions
- **Slice 05 (`05-item-catalog` — ACCEPTED)**: `Unit`, hybrid `UnitConversionGraph` (`UnitConversion` + `ItemUnitConversion`), and `Item.BaseUnitId`.
- **Slice 06 (`06-production-orders` — ACCEPTED)**: `ProductionOrder` snapshots (`ProductionOrderInput`, `ProductionOrderOutput`, `ActualProducedQuantity`, `ActualScrapQuantity`, lifecycle statuses).
- **Slice 07 (`07-inventory` — ACCEPTED)**: `Warehouse`, `InventoryBalance`, `InventoryTransaction`, `InventoryLedgerEntry` (`ProductionIssue`, `ProductionReceipt`, `Reversal`).

### Out-of-scope (deferred to later slices)
- Standard vs Actual Cost valuation, FIFO/LIFO GL journals, and financial profitability reporting.
- Automatic Purchase Requisition / Transfer Order creation from shortages (MRP execution/planning workflow).
- Background scheduled PDF/Email report subscriptions and custom user-built report designers.

## 2. Operation x Permission x Scope matrix

| Permission code | Scope applicability | Covered operations |
|---|---|---|
| `Reports.Read` | `OrganizationOrSite` | Query Material Shortage / Net Requirements report, Production Consumption & Yield Variance summary list, and per-order Input/Output variance breakdown within authorized org/site(s) |
| `Reports.Export` | `OrganizationOrSite` | Generate UTF-8 BOM + formula-injection-neutralized CSV export for Material Shortage or Production Variance within authorized org/site(s) |

## 3. Core Calculation Rules
- **Unit Normalization**: Every material input line (`ProductionOrderInput`) and planned output line (`ProductionOrderOutput`) is converted from its stored `UnitId` to `Item.BaseUnitId` via `UnitConversionGraph.ConvertQuantity(...)` before aggregating or comparing with `InventoryBalance.OnHandBaseQuantity` and `InventoryLedgerEntry.BaseQuantityDelta`.
- **Material Shortage / Net Requirements**:
  - Demand sources: `ProductionOrder` with `Status == Released || Status == InProgress` matching optional `SiteId` and due-date filters; lines where `!IsSubAssembly`.
  - Supply sources: `InventoryBalance.OnHandBaseQuantity` across warehouses belonging to the queried authorized site scope (only active warehouses / all physical balances in those sites — positive physical balances across all warehouses of the authorized site(s), regardless of warehouse active flag so physical stock is never hidden).
  - Net metrics per item (grouped by `(ItemId)` across selected site scope, or filterable by site):
    - `RequiredBaseQuantity = Sum(GrossQuantity converted to BaseUnitId)`
    - `OnHandBaseQuantity = Sum(InventoryBalance.OnHandBaseQuantity)`
    - `ShortageBaseQuantity = Max(0, RequiredBaseQuantity - OnHandBaseQuantity)`
    - `ExcessBaseQuantity = Max(0, OnHandBaseQuantity - RequiredBaseQuantity)`
    - `CoveragePercent = RequiredBaseQuantity > 0 ? Min(100, Round(OnHandBaseQuantity / RequiredBaseQuantity * 100, 2)) : 100`
- **Production Consumption & Yield Variance**:
  - Order-level actuals: `ActualProducedBaseQuantity` (`ActualProducedQuantity` converted to item `BaseUnitId`), `ActualScrapBaseQuantity`, `PostedReceivedBaseQuantity` (sum of positive/compensating `BaseQuantityDelta` across `ProductionReceipt` and reversals of `ProductionReceipt` for that `ProductionOrderId`), and `YieldPercent = PlannedBaseQuantity > 0 ? Round(ActualProducedBaseQuantity / PlannedBaseQuantity * 100, 2) : 0`.
  - Per-input material line (`!IsSubAssembly` grouped by `ItemId` or explicit line):
    - `PlannedBaseQuantity` vs `IssuedBaseQuantity` (`-Sum(BaseQuantityDelta)` across `ProductionIssue` and reversals of `ProductionIssue` for `(ProductionOrderId, ItemId)`).
    - `VarianceBaseQuantity = IssuedBaseQuantity - PlannedBaseQuantity` (`> 0` = over-consumption, `< 0` = under-issued / open).
  - Per-output line (`ProductionOrderOutput` grouped by `(ItemId, OutputType)`):
    - `PlannedBaseQuantity` vs `ReceivedBaseQuantity` (`Sum(BaseQuantityDelta)` across `ProductionReceipt` and reversals of `ProductionReceipt`).
- **CSV Export Safety (`ReportingUseCases`)**:
  - UTF-8 BOM (`0xEF, 0xBB, 0xBF`) so Excel opens Persian UTF-8 cleanly.
  - RFC-4180 quoting (`"`, `,`, `\r`, `\n`).
  - Spreadsheet formula-injection guard: any cell starting with `=`, `+`, `-`, `@`, `\t`, or `\r` (after ignoring leading whitespace if appropriate, or checking first char + trimmed start) is prefixed with `'` before CSV quoting.

## 4. UI & Localization Standards
- Route: `/reports` and `/organizations/{OrganizationId:long}/reports` (`ReportsNavGroup` in `MainLayout`).
- `Bit.BlazorUI 10.7.0`: KPI summary cards, Scope & Site/Status/Date filter bar, Tabs (**Material Shortage**, **Production Variance**), `BitDataGrid` (no `Class` on `<BitDataGridColumn>`), detail `BitDialog` (`ShowOkButton="false" ShowCancelButton="false"`), and Download CSV action gated on `Reports.Export`.
- Bilingual `fa-IR` / `en-US` `.resx` resources covering 100% of labels, tabs, statuses, CSV headers, and error codes (`ResourceContractTests` verified).

## 5. Implementation Status & Next Step
- **08.0 Design**: APPROVED (`Q-R01`–`Q-R05`).
- **08.1 Domain**: Added `PermissionModule.Reports = 11` (`Reports.Read`, `Reports.Export` as `OrganizationOrSite`, granted to built-in `OrganizationAdministrator` in `PermissionCatalog`) and `ReportingErrors` / `ReportingRuleException`.
- **08.2 Application**: Implemented [ReportingContracts.cs](../../../src/ForgeFlow.Application/Reporting/ReportingContracts.cs) & [ReportingUseCases.cs](../../../src/ForgeFlow.Application/Reporting/ReportingUseCases.cs) (Material Shortage net requirements via `UnitConversionGraph`, Production Consumption & Yield Variance netting `ProductionIssue`/`ProductionReceipt` with `Reversal`, site isolation, and UTF-8 BOM + formula-injection-neutralized CSV generation).
- **08.3 Infrastructure**: Added `SqlReportingQueryStore` (`AsNoTracking()` read-only SQL queries, zero new tables/migrations), updated `RuntimeSessions` read checks for `Reports.Read`/`Reports.Export`, and registered `ReportingUseCases`.
- **08.4 Web & Tests**: Added `LoadReportScopesAsync` in `ReadScopes`, `ReportsNavGroup` -> `/reports` (`Reports.razor` + `report-download.js`), `RolePermissionLocalization`, `fa-IR`/`en-US` `.resx` keys, `ReportTests` (`R01`–`R06`), and `ReportSqlTests` (`S01`–`S02`).
- **Next**: Run `ReportSqlTests` with `$env:FORGEFLOW_TEST_SQLSERVER` set, then verify `/reports` (both tabs, drill-down dialogs, CSV export, `fa-IR`/`en-US` RTL/LTR, desktop + mobile) in `https://localhost:5001` before accepting Slice 08.
