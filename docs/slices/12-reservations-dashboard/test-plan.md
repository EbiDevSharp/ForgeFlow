# Test Plan — Slice 12 (`12-reservations-dashboard`)

| ID | Scope | Coverage |
|---|---|---|
| **R01** | Domain (`ReservationTests`) | `InventoryBalance.AvailableBaseQuantity = OnHand - Blocked - Reserved`, reserving above available stock fails with `InsufficientStock`, `StockReservation.Consume` / `Release` state transitions & bounds. |
| **R02** | Application (`DashboardTests`) | `DashboardUseCases` scope resolution, site enforcement, and per-module `*.Read` authorization filtering (unauthorized sections return `null` / empty rather than throwing or leaking data). |
| **S01** | SQL Integration (`ReservationSqlTests`) | Reserve explicit lot & FIFO auto-allocate on Released Production Order; second order blocked from stealing reserved free stock (`InsufficientStock`); `ProductionIssue` automatically consumes active reservation & decrements `ReservedBaseQuantity`; `CancelOrder`/`CompleteOrder` auto-releases remaining reservation; `MaterialShortageReport` reflects `ReservedBaseQuantity` & reduced `AvailableBaseQuantity`; `StockReservation` audit events verified in `OrganizationChange`. |
| **S02** | SQL Schema & Dashboard (`OrganizationPersistenceModelTests`, `OrganizationSqlServerTests`) | 39 business entities, 11 migrations, full up & rollback `0 -> 11 -> 0` clean on disposable SQL DB; live `DashboardUseCases.GetSnapshot` queries real SQL rows. |
| **UI** | Localization & Browser (`ResourceContractTests`, `HardcodedDisplayTextScannerTests`, `browser-check.cjs`) | 100% `.resx` key parity (`en-US` & `fa-IR`), zero hardcoded display text, 320px/1440px responsive Home Dashboard & reservation flows. |
