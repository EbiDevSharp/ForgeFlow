# Slice 14 Physical ERD (`organization` schema)

Total business entities after Slice 14: **`47`** (`42` existing + `5` new).

## New Tables
1. `organization.Customer`
   - PK `PK_Customer` on `(Id)`; AK `AK_Customer_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `Code` (`varchar(32)`, `Latin1_General_100_BIN2`), `Name` (`nvarchar(150)`), `ContactName` (`nvarchar(120)` nullable), `Phone` (`varchar(32)` nullable), `Email` (`nvarchar(200)` nullable), `ShippingAddress` (`nvarchar(500)` nullable), `TaxCode` (`nvarchar(40)` nullable), `Notes` (`nvarchar(1000)` nullable), `IsActive` (`bit`), audit columns + `RowVersion`.
   - Unique index `UX_Customer_Org_Code` on `(OrganizationId, Code)`.

2. `organization.SalesOrder`
   - PK `PK_SalesOrder` on `(Id)`; AK `AK_SalesOrder_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `SiteId`, `CustomerId`, `OrderNumber` (`varchar(32)`, `Latin1_General_100_BIN2`), `Status` (`tinyint`, `1..6`), `OrderDate` (`date`), `RequestedDeliveryDate` (`date` nullable), `SourceWarehouseId` (`bigint` nullable), `CurrencyCode` (`varchar(3)`), `ShippingAddressSnapshot` (`nvarchar(500)` nullable), `Notes` (`nvarchar(1000)` nullable), `CancellationReason` (`nvarchar(300)` nullable), `ApprovedAtUtc` (`datetime2` nullable), `ClosedAtUtc` (`datetime2` nullable), `CancelledAtUtc` (`datetime2` nullable), audit columns + `RowVersion`.
   - Unique index `UX_SalesOrder_Org_OrderNumber` on `(OrganizationId, OrderNumber)`.
   - FKs (`NoAction`): `(OrganizationId, SiteId) -> Site`, `(OrganizationId, CustomerId) -> Customer`, `(OrganizationId, SourceWarehouseId) -> Warehouse`.
   - Checks: `CK_SalesOrder_Status` (`[Status] IN (1,2,3,4,5,6)`), `CK_SalesOrder_Dates` (`[RequestedDeliveryDate] IS NULL OR [RequestedDeliveryDate] >= [OrderDate]`).

3. `organization.SalesOrderLine`
   - PK `PK_SalesOrderLine` on `(Id)`; AK `AK_SalesOrderLine_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `SalesOrderId`, `LineNumber` (`int > 0`), `ItemId`, `OrderedQuantity` (`decimal(18,6) > 0`), `OrderedUnitId`, `BaseUnitIdSnapshot`, `OrderedBaseQuantity` (`decimal(18,6) > 0`), `DispatchedBaseQuantity` (`decimal(18,6) >= 0`), `UnitPrice` (`decimal(18,4)` nullable), `Notes` (`nvarchar(300)` nullable).
   - Unique index `UX_SalesOrderLine_Order_LineNumber` on `(OrganizationId, SalesOrderId, LineNumber)`.
   - FKs (`NoAction`): `(OrganizationId, SalesOrderId) -> SalesOrder`, `(OrganizationId, ItemId) -> Item`, `(OrganizationId, OrderedUnitId) -> Unit`, `(OrganizationId, BaseUnitIdSnapshot) -> Unit`.
   - Checks: `CK_SalesOrderLine_Qty` (`[OrderedQuantity] > 0 AND [OrderedBaseQuantity] > 0 AND [DispatchedBaseQuantity] >= 0 AND [DispatchedBaseQuantity] <= [OrderedBaseQuantity]`).

4. `organization.DeliveryOrder`
   - PK `PK_DeliveryOrder` on `(Id)`; AK `AK_DeliveryOrder_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `SiteId`, `SalesOrderId`, `WarehouseId`, `DispatchNumber` (`varchar(32)`, `Latin1_General_100_BIN2`), `Status` (`tinyint`, `1=Posted`, `2=Reversed`), `DispatchDate` (`date`), `CarrierName` (`nvarchar(120)` nullable), `TrackingNumber` (`nvarchar(64)` nullable), `Notes` (`nvarchar(1000)` nullable), `PostedAtUtc` (`datetime2`), `ReversedAtUtc` (`datetime2` nullable), `ReversalReason` (`nvarchar(300)` nullable), audit columns + `RowVersion`.
   - Unique index `UX_DeliveryOrder_Org_DispatchNumber` on `(OrganizationId, DispatchNumber)`.
   - FKs (`NoAction`): `(OrganizationId, SiteId) -> Site`, `(OrganizationId, SalesOrderId) -> SalesOrder`, `(OrganizationId, WarehouseId) -> Warehouse`.
   - Check: `CK_DeliveryOrder_Status` (`[Status] IN (1,2)`).

5. `organization.DeliveryOrderLine`
   - PK `PK_DeliveryOrderLine` on `(Id)`.
   - Columns: `OrganizationId`, `DeliveryOrderId`, `SalesOrderLineId`, `LineNumber` (`int > 0`), `ItemId`, `LotCode` (`nvarchar(64)`), `DispatchedBaseQuantity` (`decimal(18,6) > 0`), `BaseUnitIdSnapshot`, `QualityInspectionId` (`bigint` nullable), `CertificateNumberSnapshot` (`nvarchar(64)` nullable), `Notes` (`nvarchar(300)` nullable).
   - Unique index `UX_DeliveryOrderLine_Order_LineNumber` on `(OrganizationId, DeliveryOrderId, LineNumber)`.
   - FKs (`NoAction`): `(OrganizationId, DeliveryOrderId) -> DeliveryOrder`, `(OrganizationId, SalesOrderLineId) -> SalesOrderLine`, `(OrganizationId, ItemId) -> Item`, `(OrganizationId, BaseUnitIdSnapshot) -> Unit`, `(OrganizationId, QualityInspectionId) -> QualityInspection`.

## Extended Existing Tables
- `organization.StockReservation`:
  - Add `SourceKind` (`tinyint`, default `1`: `1=ProductionOrder`, `2=SalesOrder`).
  - Alter `ProductionOrderId` to nullable; add nullable `SalesOrderId` and `SalesOrderLineId`.
  - FKs (`NoAction`): `(OrganizationId, SalesOrderId) -> SalesOrder`, `(OrganizationId, SalesOrderLineId) -> SalesOrderLine`.
  - Check `CK_StockReservation_Source`:
    `([SourceKind] = 1 AND [ProductionOrderId] IS NOT NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL) OR ([SourceKind] = 2 AND [SalesOrderId] IS NOT NULL AND [ProductionOrderId] IS NULL AND [ProductionOrderInputId] IS NULL)`.
- `organization.InventoryTransaction`:
  - Add nullable `DeliveryOrderId` (`bigint`).
  - Index `IX_InventoryTransaction_Org_DeliveryOrder` on `(OrganizationId, DeliveryOrderId)`.
  - FK (`NoAction`): `(OrganizationId, DeliveryOrderId) -> DeliveryOrder`.
- `organization.AuditEvent`:
  - Extend `EventCode` `CHECK` constraint with `Customer.*`, `SalesOrder.*`, and `DeliveryOrder.*` event codes.
