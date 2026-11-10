# Slice 06 — Physical ERD & Database Contract (`06-production-orders`)

All new tables belong to schema `organization` and follow Slice 01–05 physical conventions:
- Composite `(OrganizationId, ...)` foreign keys preventing cross-tenant references.
- Standard audit metadata columns (`CreatedAtUtc`, `CreatedByActorId`, `UpdatedAtUtc`, `UpdatedByActorId`, `RowVersion` rowversion concurrency token) enforced with SQL `CHECK` constraints.
- `DeleteBehavior.NoAction` on every foreign key.
- Restricted `[ForgeFlowOrganizationRuntime]` SQL role: `DENY DELETE` on `ProductionOrder`; `GRANT SELECT, INSERT, UPDATE, DELETE` on draft-replaceable child snapshot tables (`ProductionOrderInput`, `ProductionOrderOutput`, `ProductionOrderContext`) guarded by Domain lifecycle (`Status == Draft`).
- Atomic audit persistence via `organization.OrganizationChange` (extending `CK_OrganizationChange_Event` for site-scoped `ProductionOrder.*` events: `ProductionOrder.Created`, `ProductionOrder.Updated`, `ProductionOrder.Refreshed`, `ProductionOrder.Released`, `ProductionOrder.Started`, `ProductionOrder.Completed`, `ProductionOrder.Cancelled`).

## Tables

### 1. `[organization].[ProductionOrder]`
- `Id` (`bigint IDENTITY PK`)
- `OrganizationId` (`bigint NOT NULL`) — FK `[organization].[Organization]`
- `SiteId` (`bigint NOT NULL`) — composite FK `(OrganizationId, SiteId)` → `[organization].[Site]`
- `OrderNumber` (`varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL`) — unique `(OrganizationId, OrderNumber)` (`UX_ProductionOrder_Org_Number`); ASCII code check matching `Code` rules
- `Status` (`tinyint NOT NULL`) — `CHECK ([Status] BETWEEN 1 AND 5)` (`1=Draft, 2=Released, 3=InProgress, 4=Completed, 5=Cancelled`)
- `ItemId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ItemId)` → `[organization].[Item]`
- `RequestedQuantity` (`decimal(18,6) NOT NULL`) — `CHECK ([RequestedQuantity] > 0)`
- `RequestedUnitId` (`bigint NOT NULL`) — composite FK `(OrganizationId, RequestedUnitId)` → `[organization].[Unit]`
- `PlannedStartDate` (`date NOT NULL`)
- `PlannedDueDate` (`date NOT NULL`) — `CHECK ([PlannedDueDate] >= [PlannedStartDate])`
- **Frozen BOM Traceability (`Day 11`)**:
  - `BomId` (`bigint NOT NULL`) — composite FK `(OrganizationId, BomId)` → `[organization].[Bom]`
  - `BomCodeSnapshot` (`varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL`)
  - `BomVersionId` (`bigint NOT NULL`) — composite FK `(OrganizationId, BomVersionId)` → `[organization].[BomVersion]`
  - `BomVersionNumberSnapshot` (`int NOT NULL`)
  - `BaseOutputQuantitySnapshot` (`decimal(18,6) NOT NULL`) — `CHECK (> 0)`
  - `BaseOutputUnitIdSnapshot` (`bigint NOT NULL`) — composite FK `(OrganizationId, BaseOutputUnitIdSnapshot)` → `[organization].[Unit]`
- **Execution Tracking**:
  - `ActualProducedQuantity` (`decimal(18,6) NOT NULL DEFAULT 0`) — `CHECK (>= 0)`
  - `ActualScrapQuantity` (`decimal(18,6) NOT NULL DEFAULT 0`) — `CHECK (>= 0)`
  - `Notes` (`nvarchar(500) NULL`)
  - `CancellationReason` (`nvarchar(500) NULL`)
  - `ReleasedAtUtc`, `StartedAtUtc`, `CompletedAtUtc`, `CancelledAtUtc` (`datetime2(7) NULL`)
- **Indexes**:
  - `AK_ProductionOrder_Org_Id` unique `(OrganizationId, Id)`
  - `UX_ProductionOrder_Org_Number` unique `(OrganizationId, OrderNumber)`
  - `IX_ProductionOrder_Org_Site_Status_Date` `(OrganizationId, SiteId, Status, PlannedStartDate)`
  - `IX_ProductionOrder_Org_BomVersion` `(OrganizationId, BomVersionId)`

### 2. `[organization].[ProductionOrderInput]`
Frozen material requirements snapshot at order creation/release (immutable once `Status != Draft`):
- `Id` (`bigint IDENTITY PK`)
- `OrganizationId` (`bigint NOT NULL`)
- `ProductionOrderId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ProductionOrderId)` → `[organization].[ProductionOrder]`
- `Level` (`int NOT NULL`) — `CHECK ([Level] >= 1)`
- `Sequence` (`int NOT NULL`) — `CHECK ([Sequence] >= 1)`
- `ParentItemId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ParentItemId)` → `[organization].[Item]`
- `ItemId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ItemId)` → `[organization].[Item]`
- `NetQuantity` (`decimal(18,6) NOT NULL`) — `CHECK ([NetQuantity] > 0)`
- `ScrapPercentage` (`decimal(5,2) NOT NULL`) — `CHECK ([ScrapPercentage] >= 0 AND [ScrapPercentage] < 100)`
- `GrossQuantity` (`decimal(18,6) NOT NULL`) — `CHECK ([GrossQuantity] > 0)`
- `UnitId` (`bigint NOT NULL`) — composite FK `(OrganizationId, UnitId)` → `[organization].[Unit]`
- `IsOptional` (`bit NOT NULL`)
- `IsSubAssembly` (`bit NOT NULL`)
- `ResolvedSubBomVersionId` (`bigint NULL`) — composite FK `(OrganizationId, ResolvedSubBomVersionId)` → `[organization].[BomVersion]` (if sub-assembly)

### 3. `[organization].[ProductionOrderOutput]`
Frozen expected outputs (`MainProduct`, `CoProduct`, `ByProduct`, `Waste`) snapshot:
- `Id` (`bigint IDENTITY PK`)
- `OrganizationId` (`bigint NOT NULL`)
- `ProductionOrderId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ProductionOrderId)` → `[organization].[ProductionOrder]`
- `Level` (`int NOT NULL`) — `CHECK ([Level] >= 1)`
- `Sequence` (`int NOT NULL`) — `CHECK ([Sequence] >= 1)`
- `ItemId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ItemId)` → `[organization].[Item]`
- `OutputType` (`tinyint NOT NULL`) — `CHECK ([OutputType] BETWEEN 1 AND 4)`
- `PlannedQuantity` (`decimal(18,6) NOT NULL`) — `CHECK ([PlannedQuantity] > 0)`
- `UnitId` (`bigint NOT NULL`) — composite FK `(OrganizationId, UnitId)` → `[organization].[Unit]`

### 4. `[organization].[ProductionOrderContext]`
Exact context key-value pairs used when resolving the BOM (including injected `PLANT = Site.Code`):
- `Id` (`bigint IDENTITY PK`)
- `OrganizationId` (`bigint NOT NULL`)
- `ProductionOrderId` (`bigint NOT NULL`) — composite FK `(OrganizationId, ProductionOrderId)` → `[organization].[ProductionOrder]`
- `DimensionCode` (`varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL`)
- `DimensionValue` (`nvarchar(400) NOT NULL`)
- Unique `(ProductionOrderId, DimensionCode)` (`UX_ProductionOrderContext_Order_Dim`)
