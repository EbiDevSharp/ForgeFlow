# Slice 14 Architecture Decisions (`S-D01`–`S-D05`)

Approved by user via `sales_dispatch_design_plan.md` (2026-11-12).

- **`S-D01` Customer Master & Sales Order Lifecycle (Mirroring Slice 10 Procurement):**
  - `Customer` is organization-scoped (`Code`, `Name`, `ContactName`, `Phone`, `Email`, `ShippingAddress`, `TaxCode`, `Notes`, `IsActive`).
  - `SalesOrder` is site-scoped (`OrderNumber`, `CustomerId`, `Status`: `Draft=1`, `Approved=2`, `PartiallyDispatched=3`, `Dispatched=4`, `Cancelled=5`, `Closed=6`, `OrderDate`, `RequestedDeliveryDate`, optional `SourceWarehouseId`, `CurrencyCode`, `ShippingAddressSnapshot`, `Notes`, `CancellationReason`).
  - `SalesOrderLine` snapshots `OrderedUnitId`, `BaseUnitIdSnapshot`, `OrderedBaseQuantity`, tracks `DispatchedBaseQuantity`, and computes `RemainingBaseQuantity`.

- **`S-D02` Unified `StockReservation` Extension (`Slice 12`):**
  - Extend `organization.StockReservation` rather than introducing a separate sales reservation table:
    - Add `SourceKind` (`tinyint`: `1=ProductionOrder`, `2=SalesOrder`).
    - Make `ProductionOrderId` nullable; add nullable `SalesOrderId` and `SalesOrderLineId`.
    - Enforce `CK_StockReservation_Source` XOR check constraint so each reservation targets either a Production Order or a Sales Order.
  - Posting a `DeliveryOrder` auto-consumes matching open `SalesOrder` reservations first, then deducts any remaining required quantity from unreserved `AvailableBaseQuantity`.
  - Cancelling or closing a `SalesOrder` automatically releases all remaining open reservations back to `AvailableBaseQuantity`.

- **`S-D03` Dispatch / Delivery Order (`DeliveryOrder` & `DeliveryOrderLine`) with Quality & COA Gates:**
  - `DeliveryOrder` is site-scoped (`DispatchNumber`, `SalesOrderId`, `WarehouseId`, `DispatchDate`, `Status`: `Posted=1`, `Reversed=2`, `CarrierName`, `TrackingNumber`, `Notes`, `PostedAtUtc`, `ReversedAtUtc`, `ReversalReason`).
  - Posting a `DeliveryOrder` atomically:
    1. Verifies `SalesOrder` is `Approved` or `PartiallyDispatched` and prevents over-dispatch (`DispatchedBaseQuantity + delta <= OrderedBaseQuantity`).
    2. Enforces **Expiry Gate (`Slice 09`)**: rejects lots where `ItemLot.IsExpiredOn(DispatchDate)` is true.
    3. Enforces **Quarantine / Availability Gate (`Slice 07`/`09`)**: available stock + open `SalesOrder` reservation for that line must cover the dispatch quantity (`BlockedBaseQuantity` is never dispatchable).
    4. Auto-resolves and snapshots the latest `Completed` `QualityInspection` (`QualityInspectionId`, `CertificateNumberSnapshot`) for `(OrganizationId, ItemId, LotCode)` onto `DeliveryOrderLine` (`Slice 13` COA reference).
    5. Consumes matching `StockReservation`s, deducts `InventoryBalance.OnHandBaseQuantity`, writes `InventoryTransaction` (`InventoryTransactionType.SalesDispatch = 10`, `DeliveryOrderId`), and updates `SalesOrderLine.DispatchedBaseQuantity` + `SalesOrder.Status`.
  - `ReverseDispatch` posts compensating `InventoryTransactionType.Reversal` entries, restores `InventoryBalance.OnHandBaseQuantity`, and reverts `SalesOrderLine.DispatchedBaseQuantity` / `SalesOrder.Status` (when `SalesOrder` is not `Closed` or `Cancelled`).

- **`S-D04` Permissions & Audit Events:**
  - Add `PermissionModule.Sales = 16` in `PermissionCatalog.cs`:
    - `Sales.Read` (`OrganizationOrSite`) — also grants read-only access to `QualitySpecUseCases.GetLotCertificate`.
    - `Sales.ManageCustomers` (`OrganizationOnly`).
    - `Sales.ManageOrders` (`OrganizationOrSite`).
    - `Sales.Dispatch` (`OrganizationOrSite`).
  - Register `Customer.*`, `SalesOrder.*`, and `DeliveryOrder.*` event codes in `AuditEventCatalog.cs` and in `OrganizationModel.cs`'s `AuditEvent` `CHECK` constraint.

- **`S-D05` UI & Localization (`ForgeFlow.Web`):**
  - Add `/sales` (`Sales.razor`) in the Operations navigation group (`MainLayout.razor`) with 3 tabs: **Customers**, **Sales Orders**, and **Dispatches**, plus Reserve Stock dialog, Post Dispatch dialog (lot picker with available qty, expiry, QC status & COA badge), and `LotCertificateDialog` integration.
  - Full `en-US`/`fa-IR` `.resx` localization, RTL/LTR, Light/Dark, and `320px` mobile support.
