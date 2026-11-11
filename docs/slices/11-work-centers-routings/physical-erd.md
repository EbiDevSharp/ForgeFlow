# Slice 11 — Physical ERD (`11-work-centers-routings`)

## Tables in Schema `[organization]`
1. **`[organization].[WorkCenter]`**
   - Keys: `PK_WorkCenter (Id)`, `AK_WorkCenter_Org_Id (OrganizationId, Id)`, `AK_WorkCenter_Org_Site_Id (OrganizationId, SiteId, Id)`
   - Unique: `UX_WorkCenter_Org_Site_Code (OrganizationId, SiteId, Code)`
   - Checks: `CK_WorkCenter_Code`, `CK_WorkCenter_Name`, `CK_WorkCenter_Type ([WorkCenterType] BETWEEN 1 AND 5)`, `CK_WorkCenter_Capacity ([CapacityUnitsPerHour] > 0)`, `CK_WorkCenter_Hours ([DailyAvailableHours] > 0 AND [DailyAvailableHours] <= 24)`, `CK_WorkCenter_Cost ([HourlyCostRate] IS NULL OR [HourlyCostRate] >= 0)`, `CK_WorkCenter_Metadata`
   - FKs: `Organization`, `Site (OrganizationId, SiteId)`

2. **`[organization].[Routing]`**
   - Keys: `PK_Routing (Id)`, `AK_Routing_Org_Id (OrganizationId, Id)`, `AK_Routing_Org_Site_Id (OrganizationId, SiteId, Id)`
   - Unique: `UX_Routing_Org_Site_Code (OrganizationId, SiteId, Code)`, `UX_Routing_Org_Site_Item_Ver (OrganizationId, SiteId, ItemId, VersionNumber)`
   - Checks: `CK_Routing_Code`, `CK_Routing_Name`, `CK_Routing_Version ([VersionNumber] >= 1)`, `CK_Routing_Status ([Status] BETWEEN 1 AND 3)`, `CK_Routing_Metadata`
   - FKs: `Organization`, `Site (OrganizationId, SiteId)`, `Item (OrganizationId, ItemId)`, `BomVersion (OrganizationId, BomVersionId)` (optional)

3. **`[organization].[RoutingOperation]`**
   - Key: `PK_RoutingOperation (Id)`
   - Unique: `UX_RoutingOperation_Org_Routing_Seq (OrganizationId, RoutingId, Sequence)`
   - Checks: `CK_RoutingOperation_Seq ([Sequence] > 0)`, `CK_RoutingOperation_Code`, `CK_RoutingOperation_Name`, `CK_RoutingOperation_Times ([SetupMinutes] >= 0 AND [RunMinutesPerUnit] > 0 AND [QueueMinutes] >= 0 AND [WaitMinutes] >= 0)`, `CK_RoutingOperation_Metadata`
   - FKs: `Routing (OrganizationId, SiteId, RoutingId)`, `WorkCenter (OrganizationId, SiteId, WorkCenterId)`

4. **`[organization].[ProductionOrderOperation]`**
   - Key: `PK_ProductionOrderOperation (Id)`
   - Unique: `UX_ProductionOrderOperation_Org_Po_Seq (OrganizationId, ProductionOrderId, Sequence)`
   - Checks: `CK_ProductionOrderOperation_Seq ([Sequence] > 0)`, `CK_ProductionOrderOperation_Code`, `CK_ProductionOrderOperation_Name`, `CK_ProductionOrderOperation_Status ([Status] BETWEEN 1 AND 6)`, `CK_ProductionOrderOperation_Times`, `CK_ProductionOrderOperation_Quantities`, `CK_ProductionOrderOperation_Metadata`
   - FKs: `ProductionOrder (OrganizationId, SiteId, ProductionOrderId)`, `WorkCenter (OrganizationId, SiteId, WorkCenterId)`

5. **`[organization].[ProductionOrder]`** (altered)
   - Nullable columns: `RoutingId (bigint NULL)`, `RoutingCodeSnapshot (varchar(32) NULL)`
   - FK: `FK_ProductionOrder_Routing (OrganizationId, SiteId, RoutingId)`
