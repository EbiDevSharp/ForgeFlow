# Slice 14 — Sales, Customers & Dispatch

## Scope (`14-sales-dispatch`)
Complete ForgeFlow's **Order-to-Cash** loop alongside existing **Procure-to-Pay** (`10-procurement`) and **Plan-to-Produce** (`06-production-orders`, `11-work-centers-routings`) workflows.

Organizations can manage **Customers**, create and approve **Sales Orders** (`SO`) with multi-unit line items and requested delivery dates, reserve available lot stock (`12-reservations-dashboard`), post **Dispatch / Delivery Orders** (`DeliveryOrder`) from non-quarantined, non-expired lots (`07-inventory`, `09-quality-traceability`) with automatic **Lot Certificate of Analysis (`COA`)** reference (`13-lab-specs-coa`), and reverse dispatches when needed.

## Cross-Slice Integrations
- **Master Data & Units (`05-item-catalog`)**: Multi-unit sales ordering converted to item base unit snapshots via `UnitConversionGraph` / `ItemUnitConversion`, plus organization-configured `CurrencyCode`.
- **Inventory Ledger (`07-inventory`)**: Atomic outbound deduction of `InventoryBalance.OnHandBaseQuantity` and append-only `InventoryTransaction` (`SalesDispatch = 10` / `Reversal = 8`) linked to `DeliveryOrderId`.
- **Quality & Lot Expiry (`09-quality-traceability`)**: Blocks dispatch from expired lots (`ItemLot.ExpirationDate < DispatchDate`) and quarantined/held stock (`BlockedBaseQuantity`).
- **Stock Reservations (`12-reservations-dashboard`)**: Extends `StockReservation` to support `SalesOrder` allocations alongside `ProductionOrder` allocations, with automatic consumption on dispatch and automatic release on order close/cancel.
- **Lot COA (`13-lab-specs-coa`)**: Auto-resolves and snapshots the latest completed `QualityInspection` (`CertificateNumber`) onto `DeliveryOrderLine` and exposes `LotCertificateDialog` to `Sales.Read` actors.

## Increments
- `14.0`: Slice design package (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`).
- `14.1`: Domain (`Customer`, `SalesOrder`, `SalesOrderLine`, `SalesOrderStatus`, `DeliveryOrder`, `DeliveryOrderLine`, `DeliveryOrderStatus`, `SalesErrors`, `SalesText`, `StockReservation` `ReservationSourceKind` extension, `InventoryTransactionType.SalesDispatch`, `PermissionModule.Sales` in `PermissionCatalog`).
- `14.2`: Application (`SalesContracts.cs`, `SalesUseCases.cs`, `ReservationContracts.cs`/`StockReservationUseCases.cs` extension for Sales Orders, `QualitySpecUseCases.cs` `Sales.Read` COA access, `AuditEventCatalog.cs`).
- `14.3`: Infrastructure & EF Migration (`OrganizationModel`, `OrganizationDbContext`, `AuthDbContext`, `SqlSalesStore`, `SqlStockReservationStore` extension, migration + `localhost/testforgflow` upgrade).
- `14.4`: Web UI & Localization (`Sales.razor` at `/sales` with Customers, Sales Orders, Dispatches tabs, Reserve & Dispatch dialogs, `LotCertificateDialog` integration, `MainLayout.razor` nav link, `.resx`, `ResourceContractTests.cs`, `app.css`).
- `14.5`: Unit + SQL tests (`SalesDispatchTests.cs`, `SalesDispatchSqlTests.cs`, updated model/SQL table & migration counts).
- `14.6`: Live UI review (`fa-IR`/`en-US`, Light/Dark, RTL/LTR, mobile `320px`) & acceptance.
