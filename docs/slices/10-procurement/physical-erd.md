# Slice 10 — Physical ERD & Schema Contract (`10-procurement`)

## Tables Added in `[organization]` Schema
1. **`[organization].[Supplier]`**
   - `Id` (`bigint` PK IDENTITY), `OrganizationId` (`bigint`), `Code` (`varchar(32)`, BIN2, unique per org), `Name` (`nvarchar(200)`), `ContactName` (`nvarchar(150)` NULL), `Phone` (`varchar(64)` NULL), `Email` (`nvarchar(256)` NULL), `Address` (`nvarchar(500)` NULL), `Notes` (`nvarchar(500)` NULL), `IsActive` (`bit`), audit metadata, `RowVersion`.
   - `AK_Supplier_Org_Id (OrganizationId, Id)`, `UX_Supplier_Org_Code (OrganizationId, Code)`.
   - Checks: `CK_Supplier_Code`, `CK_Supplier_Name`, `CK_Supplier_Metadata`.

2. **`[organization].[PurchaseOrder]`**
   - `Id` (`bigint` PK IDENTITY), `OrganizationId` (`bigint`), `SiteId` (`bigint`), `SupplierId` (`bigint`), `OrderNumber` (`varchar(32)`, BIN2, unique per org), `Status` (`tinyint` 1..6), `OrderDate` (`date`), `ExpectedDeliveryDate` (`date` NULL), `TargetWarehouseId` (`bigint` NULL), `CurrencyCode` (`varchar(3)`), `Notes` (`nvarchar(500)` NULL), `CancellationReason` (`nvarchar(500)` NULL), `ApprovedAtUtc`, `ClosedAtUtc`, `CancelledAtUtc`, audit metadata, `RowVersion`.
   - `AK_PurchaseOrder_Org_Id (OrganizationId, Id)`, `UX_PurchaseOrder_Org_Number (OrganizationId, OrderNumber)`, `IX_PurchaseOrder_Org_Site_Status`.
   - Composite FKs: `Organization`, `(OrganizationId, SiteId) -> Site`, `(OrganizationId, SupplierId) -> Supplier`, `(OrganizationId, TargetWarehouseId) -> Warehouse`.
   - Checks: `CK_PurchaseOrder_Number`, `CK_PurchaseOrder_Status`, `CK_PurchaseOrder_Dates`, `CK_PurchaseOrder_Currency`, `CK_PurchaseOrder_Metadata`.

3. **`[organization].[PurchaseOrderLine]`**
   - `Id` (`bigint` PK IDENTITY), `OrganizationId` (`bigint`), `PurchaseOrderId` (`bigint`), `LineNumber` (`int` >= 1), `ItemId` (`bigint`), `OrderedQuantity` (`decimal(18,6)` > 0), `OrderedUnitId` (`bigint`), `BaseUnitIdSnapshot` (`bigint`), `OrderedBaseQuantity` (`decimal(18,6)` > 0), `ReceivedBaseQuantity` (`decimal(18,6)` >= 0 and <= `OrderedBaseQuantity`), `UnitPrice` (`decimal(18,4)` NULL >= 0), `Notes` (`nvarchar(300)` NULL), audit metadata, `RowVersion`.
   - `UX_PurchaseOrderLine_Org_Order_Line (OrganizationId, PurchaseOrderId, LineNumber)`, `IX_PurchaseOrderLine_Org_Item`.
   - Composite FKs: `(OrganizationId, PurchaseOrderId) -> PurchaseOrder`, `(OrganizationId, ItemId) -> Item`, `(OrganizationId, OrderedUnitId) -> Unit`, `(OrganizationId, BaseUnitIdSnapshot) -> Unit`.
   - Checks: `CK_PurchaseOrderLine_Num`, `CK_PurchaseOrderLine_Qty`, `CK_PurchaseOrderLine_Price`, `CK_PurchaseOrderLine_Metadata`.

## Extended Existing Tables
- **`[organization].[InventoryTransaction]`**: nullable `PurchaseOrderId` (`bigint` NULL), FK `(OrganizationId, PurchaseOrderId) -> PurchaseOrder` (`FK_InventoryTransaction_PurchaseOrder`), index `IX_InventoryTransaction_Org_PurchaseOrder`.
- **`[organization].[OrganizationChange]`**: `CK_OrganizationChange_Event` extended with `Supplier.*`, `PurchaseOrder.*` events (`SiteId IS NULL` for `Supplier.*`, `SiteId IS NOT NULL` for `PurchaseOrder.*`).
- **`[ForgeFlowOrganizationRuntime]`**: `GRANT SELECT, INSERT, UPDATE` and `DENY DELETE` on `Supplier`, `PurchaseOrder`, `PurchaseOrderLine`.
