# Physical ERD — Slice 12 (`organization.InventoryBalance` update & `organization.StockReservation`)

## Altered Table: `organization.InventoryBalance`
- Added column: `[ReservedBaseQuantity] decimal(18,6) NOT NULL DEFAULT 0`
- Updated `CK_InventoryBalance_Quantities`:
  `[OnHandBaseQuantity] >= 0 AND [BlockedBaseQuantity] >= 0 AND [ReservedBaseQuantity] >= 0 AND ([BlockedBaseQuantity] + [ReservedBaseQuantity]) <= [OnHandBaseQuantity]`

## New Table: `organization.StockReservation`
- `Id bigint IDENTITY PRIMARY KEY (PK_StockReservation)`
- `OrganizationId bigint NOT NULL` -> `FK_StockReservation_Organization`
- `SiteId bigint NOT NULL` -> `FK_StockReservation_Site (OrganizationId, Id)`
- `WarehouseId bigint NOT NULL` -> `FK_StockReservation_Warehouse (OrganizationId, SiteId, Id)`
- `ProductionOrderId bigint NOT NULL` -> `FK_StockReservation_Order (OrganizationId, Id)`
- `ProductionOrderInputId bigint NULL`
- `ItemId bigint NOT NULL` -> `FK_StockReservation_Item (OrganizationId, Id)`
- `LotCode varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT ''`
- `BaseUnitId bigint NOT NULL` -> `FK_StockReservation_Unit (OrganizationId, Id)`
- `ReservedBaseQuantity decimal(18,6) NOT NULL` (`> 0`)
- `ConsumedBaseQuantity decimal(18,6) NOT NULL DEFAULT 0` (`>= 0 AND ConsumedBaseQuantity <= ReservedBaseQuantity`)
- `Status tinyint NOT NULL` (`1=Active, 2=PartiallyConsumed, 3=Fulfilled, 4=Released`)
- `Notes nvarchar(500) NULL`
- `ReleasedAtUtc datetime2(7) NULL`
- Standard audit columns (`CreatedAtUtc`, `CreatedByActorId`, `RowVersion`, `UpdatedAtUtc`, `UpdatedByActorId`)
- Indexes:
  - `IX_StockReservation_Org_Order_Status (OrganizationId, ProductionOrderId, Status)`
  - `IX_StockReservation_Org_Site_Wh_Item_Lot_Status (OrganizationId, SiteId, WarehouseId, ItemId, LotCode, Status)`
- Security: `[ForgeFlowOrganizationRuntime]` gets `GRANT SELECT, INSERT, UPDATE` and `DENY DELETE` on `[organization].[StockReservation]`.
- Audit: `CK_OrganizationChange_Event` extended (`[SiteId] IS NOT NULL`) with `'InventoryReservation.Created'`, `'InventoryReservation.Consumed'`, `'InventoryReservation.Released'`.
