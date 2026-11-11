# Slice 10 — Test Plan (`10-procurement`)

## Unit Tests (`ProcurementTests.cs`)
- `P01`: Supplier validation (uppercase code, valid email/phone, active toggle guards).
- `P02`: PurchaseOrder Draft/Approved/PartiallyReceived/Received/Closed/Cancelled state machine, empty line rejection, `CanBePurchased` enforcement, no-over-receipt invariant (`ApplyReceipt` & `RevertReceipt`).
- `P03`: `ProcurementUseCases` UOM conversion on lines and receipts, rejection auditing (`Procurement.ActionRejected`), scope enforcement.

## Live SQL Server Integration Tests (`ProcurementSqlTests.cs`)
- `S01`: Org-shared Suppliers + Site-scoped PO isolation (`SiteA` actor cannot read/manage `SiteB` POs; `Procurement.ManageSuppliers` is OrganizationOnly).
- `S02`: Full lifecycle + Secondary UOM partial receipt + Idempotent retry (`Order in TON`, `Receive in KG` → `PartiallyReceived`, second receipt → `Received`, idempotent retry returns same transaction without duplicating stock).
- `S03`: PurchaseOrder Goods Receipt + Lot Metadata + Auto-QC Quarantine + Inventory Reversal (receiving lot-tracked `IncomingReceipts` item creates `ItemLot` + `QualityInspection` + blocks stock; `ReverseTransaction` cancels pending inspection and restores `PurchaseOrderLine.ReceivedBaseQuantity` and PO status; short-close transitions `PartiallyReceived -> Closed`).
- `S04`: Concurrent receipt race (`Barrier(2)` trying to receive full remaining quantity simultaneously — exactly 1 succeeds, 1 fails cleanly with `OverReceipt`/`ConcurrencyConflict`, `ReceivedBaseQuantity <= OrderedBaseQuantity`) + Audit fault-injection atomic rollback + `ReportingUseCases` `OnOrderBaseQuantity` verification.
