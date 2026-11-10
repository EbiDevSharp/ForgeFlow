# Slice 05 — Physical ERD & Schema Contract (`05-item-catalog`)

Status: **PROPOSED (`05.0`)** (2026-11-10)

## 1. Schema & DbContext boundary

All Slice 05 business tables live in `OrganizationDbContext` under the `organization` (or `catalog` / `manufacturing`) schema with composite `(OrganizationId, Id)` alternate keys so cross-organization foreign keys are impossible at the SQL Server constraint level (`docs/slices/01-organization-sites/decisions.md` `G02`). Every mutable row carries:
- `OrganizationId bigint NOT NULL` (FK to `organization.Organization(Id)` with `ON DELETE NO ACTION`)
- `CreatedAtUtc datetime2(7) NOT NULL`, `CreatedByUserId bigint NOT NULL`
- `UpdatedAtUtc datetime2(7) NULL`, `UpdatedByUserId bigint NULL`
- `RowVersion rowversion NOT NULL` (8-byte optimistic concurrency token)

Business audit events are appended in the same transaction to `organization.OrganizationChange` (extending `CK_OrganizationChange_Entity` / `CK_OrganizationChange_Type` in the additive migration to cover `Unit`, `UnitConversion`, `Item`, `ClassificationSet`, `Classification`, `AttributeDefinition`, `Bom`, and `BomVersion`).

## 2. Sub-slice `05A` — Units of Measure & Conversions

### `organization.Unit`
- `Id bigint IDENTITY(1,1) PK`
- `OrganizationId bigint NOT NULL`
- `Code varchar(32) NOT NULL` — unique per `(OrganizationId, Code)`
- `Name nvarchar(200) NOT NULL`
- `Symbol nvarchar(32) NOT NULL`
- `Dimension tinyint NOT NULL` — shipped enum `UnitDimension` (`1 = Mass`, `2 = Volume`, `3 = Length`, `4 = Area`, `5 = Count`, `6 = Time`); `CK_Unit_Dimension CHECK ([Dimension] BETWEEN 1 AND 6)`
- `PrecisionScale tinyint NOT NULL` — `0..6`; `CK_Unit_PrecisionScale CHECK ([PrecisionScale] BETWEEN 0 AND 6)`
- `IsActive bit NOT NULL`
- `RowVersion rowversion NOT NULL`
- Alternate key `AK_Unit_OrganizationId_Id (OrganizationId, Id)`

### `organization.UnitConversion`
- `Id bigint IDENTITY(1,1) PK`
- `OrganizationId bigint NOT NULL`
- `FromUnitId bigint NOT NULL`, `ToUnitId bigint NOT NULL`
- `Factor decimal(18,6) NOT NULL` — `1 FromUnit = Factor * ToUnit`
- `IsActive bit NOT NULL`
- `RowVersion rowversion NOT NULL`
- Composite FKs `(OrganizationId, FromUnitId) -> organization.Unit(OrganizationId, Id)` and `(OrganizationId, ToUnitId) -> organization.Unit(OrganizationId, Id)`
- `UX_UnitConversion_OrganizationId_From_To UNIQUE (OrganizationId, FromUnitId, ToUnitId)`
- `CK_UnitConversion_DifferentUnits CHECK ([FromUnitId] <> [ToUnitId])`
- `CK_UnitConversion_PositiveFactor CHECK ([Factor] > 0)`

## 3. Sub-slice `05B` — Item Catalog, Classifications & Attributes

### `organization.ClassificationSet` & `organization.Classification`
- `ClassificationSet`: `(Id, OrganizationId, Code, Name, IsMandatory, AllowMultiple, IsActive, RowVersion)` with `UX_ClassificationSet_Org_Code (OrganizationId, Code)` and `AK_ClassificationSet_Org_Id (OrganizationId, Id)`.
- `Classification`: `(Id, OrganizationId, ClassificationSetId, ParentClassificationId NULL, Code, Name, IsActive, RowVersion)` with composite FKs to `ClassificationSet(OrganizationId, Id)` and self-FK `(OrganizationId, ParentClassificationId) -> Classification(OrganizationId, Id)`, plus `CK_Classification_NoSelfParent CHECK ([ParentClassificationId] IS NULL OR [ParentClassificationId] <> [Id])`.

### `organization.Item`
- `Id bigint IDENTITY(1,1) PK`
- `OrganizationId bigint NOT NULL`
- `Code varchar(32) NOT NULL` — `UX_Item_OrganizationId_Code UNIQUE (OrganizationId, Code)`
- `Name nvarchar(200) NOT NULL`
- `Description nvarchar(500) NULL`
- `BaseUnitId bigint NOT NULL` — composite FK `(OrganizationId, BaseUnitId) -> organization.Unit(OrganizationId, Id)`
- `CanBePurchased bit NOT NULL`, `CanBeManufactured bit NOT NULL`, `CanBeSold bit NOT NULL`, `CanBeConsumed bit NOT NULL`
- `IsActive bit NOT NULL`
- `RowVersion rowversion NOT NULL`
- `AK_Item_OrganizationId_Id (OrganizationId, Id)`

### `organization.ItemClassification`
- `(Id, OrganizationId, ItemId, ClassificationId)` with `UX_ItemClassification_Org_Item_Class UNIQUE (OrganizationId, ItemId, ClassificationId)` and composite FKs within the same `OrganizationId`.

### `organization.ItemUnitConversion`
- `(Id, OrganizationId, ItemId, FromUnitId, ToUnitId, Factor decimal(18,6), IsActive, RowVersion)` with composite FKs to `Item` and `Unit` within `OrganizationId`, `CK_ItemUnitConversion_DifferentUnits`, and `CK_ItemUnitConversion_PositiveFactor`.

### `organization.AttributeDefinition` & `organization.ItemAttributeValue`
- `AttributeDefinition`: `(Id, OrganizationId, Code, Name, DataType tinyint [1=Text, 2=Decimal, 3=Boolean], UnitId NULL, IsRequired, IsActive, RowVersion)`.
- `ItemAttributeValue`: `(Id, OrganizationId, ItemId, AttributeDefinitionId, TextValue nvarchar(400) NULL, DecimalValue decimal(18,6) NULL, BooleanValue bit NULL, RowVersion)` with `UX_ItemAttributeValue_Org_Item_Def UNIQUE (OrganizationId, ItemId, AttributeDefinitionId)`.

## 4. Sub-slice `05C` — Manufacturing BOM, Versions, Applicability & Context

### `organization.ContextDimension`
- `(Id, OrganizationId, Code varchar(32), Name nvarchar(200), IsActive bit, RowVersion)` with `UX_ContextDimension_Org_Code UNIQUE (OrganizationId, Code)`. Built-in standard dimensions (`PLANT`, `PRODUCTION_LINE`, `MARKET`, `CUSTOMER`, `MANUFACTURING_METHOD`) can be seeded per organization on demand.

### `organization.Bom` & `organization.BomVersion`
- `Bom`: `(Id, OrganizationId, Code, Name, PrimaryOutputItemId, IsActive, RowVersion)` with composite FK `(OrganizationId, PrimaryOutputItemId) -> Item(OrganizationId, Id)`.
- `BomVersion`: `(Id, OrganizationId, BomId, VersionNumber int, Status tinyint [1=Draft, 2=Approved, 3=Active, 4=Superseded, 5=Obsolete], BaseOutputQuantity decimal(18,6), BaseOutputUnitId bigint, EffectiveFromDate date NOT NULL, EffectiveToDate date NULL, Notes nvarchar(500) NULL, RowVersion)` with:
  - `UX_BomVersion_Org_Bom_Ver UNIQUE (OrganizationId, BomId, VersionNumber)`
  - `CK_BomVersion_PositiveBaseQty CHECK ([BaseOutputQuantity] > 0)`
  - `CK_BomVersion_EffectiveDates CHECK ([EffectiveToDate] IS NULL OR [EffectiveToDate] >= [EffectiveFromDate])`

### `organization.BomInput` & `organization.BomOutput`
- `BomInput`: `(Id, OrganizationId, BomVersionId, Sequence int, ItemId bigint, Quantity decimal(18,6), UnitId bigint, ScrapPercentage decimal(5,2), IsOptional bit, Notes nvarchar(300) NULL)` with `CK_BomInput_PositiveQty CHECK ([Quantity] > 0)` and `CK_BomInput_ScrapBounds CHECK ([ScrapPercentage] >= 0 AND [ScrapPercentage] < 100)`.
- `BomOutput`: `(Id, OrganizationId, BomVersionId, Sequence int, ItemId bigint, OutputType tinyint [1=MainProduct, 2=CoProduct, 3=ByProduct, 4=Waste], Quantity decimal(18,6), UnitId bigint)` with `CK_BomOutput_PositiveQty CHECK ([Quantity] > 0)`.

### `organization.BomApplicability` & `organization.BomApplicabilityCondition`
- `BomApplicability`: `(Id, OrganizationId, BomVersionId, Name nvarchar(200), IsActive bit, RowVersion)` with `UX_BomApplicability_Org_Version UNIQUE (OrganizationId, BomVersionId)`.
- `BomApplicabilityCondition`: `(Id, OrganizationId, BomApplicabilityId, ContextDimensionId bigint, Operator tinyint [1=Equals, 2=NotEquals, 3=In, 4=NotIn], Value nvarchar(400) NOT NULL)` with `UX_BomApplicabilityCondition_Dim_Op UNIQUE (BomApplicabilityId, ContextDimensionId, Operator)`.
