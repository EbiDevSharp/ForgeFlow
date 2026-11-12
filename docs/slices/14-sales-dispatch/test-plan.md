# Slice 14 Test Plan

- **`S01` (Unit — `SalesDispatchTests.cs`)**:
  - `Customer` validates code, required name, optional email/phone/address, and active/inactive update guards.
  - `SalesOrder` and `SalesOrderLine` enforce `RequestedDeliveryDate >= OrderDate`, `Draft -> Approved -> PartiallyDispatched -> Dispatched -> Closed` transitions, over-dispatch prevention (`DispatchedBaseQuantity <= OrderedBaseQuantity`), and cancellation/close guards.
  - `DeliveryOrder` and `DeliveryOrderLine` validate positive quantities, lot code normalization, `Posted -> Reversed` state transition, and `ReversalReason` requirement.
  - `StockReservation` enforces `ReservationSourceKind` (`ProductionOrder` vs `SalesOrder`) XOR invariants, consumption, and release.
  - `PermissionCatalog` validates `Sales.Read`, `Sales.ManageCustomers` (org-only), `Sales.ManageOrders`, `Sales.Dispatch`, and `QualitySpecUseCases.GetLotCertificate` authorization for `Sales.Read`.

- **`S02` (SQL Integration — `SalesDispatchSqlTests.cs`)**:
  - End-to-end **Order-to-Cash** flow:
    - Create `Customer`, receive/inspect Lot `LOT-FG-01` (with `Completed` `QualityInspection` + `CertificateNumber` `COA-2026-01`) and expired/quarantined Lot `LOT-EXP-01` / `LOT-HOLD-01`.
    - Create & approve `SalesOrder` (`SO-001`) with multi-unit conversion (`Carton -> Unit`).
    - Reserve `LOT-FG-01` against `SO-001`, verify `InventoryBalance.ReservedBaseQuantity` increases and `AvailableBaseQuantity` decreases.
    - Verify dispatch of `LOT-EXP-01` (expired) and `LOT-HOLD-01` (blocked/quarantined) is rejected.
    - Post `DeliveryOrder` (`DO-001`) for `LOT-FG-01`: verify `StockReservation` is consumed, `InventoryBalance.OnHandBaseQuantity` decreases, `InventoryTransaction` (`SalesDispatch = 10`, `DeliveryOrderId`) is written, `DeliveryOrderLine.CertificateNumberSnapshot == "COA-2026-01"`, `SalesOrder.Status` transitions to `PartiallyDispatched`/`Dispatched`, and `GetLotCertificate` returns COA for `Sales.Read`.
    - Reverse `DO-001` (`ReverseDispatch`): verify compensating `Reversal` ledger entry, restored `InventoryBalance`, and reverted `SalesOrderLine.DispatchedBaseQuantity`.
    - Cancel a `SalesOrder` with open `StockReservation` and verify reservation is automatically released.

- **Regression & Contracts**:
  - Updated `OrganizationPersistenceModelTests` (`47` entity types, `13` migrations) & `OrganizationSqlServerTests` (`47` tables, `13` migrations).
  - Full non-SQL + HTTP test suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`), `ResourceContractTests`, `HardcodedDisplayTextScannerTests`, `drafts.test.mjs`, and `browser-check.cjs`.
  - Authenticated live UI review across `fa-IR`/`en-US`, RTL/LTR, Light/Dark, and desktop (`1440px`) / mobile (`320px`).
