# Slice 13 Physical ERD (`organization` schema)

Total business entities after Slice 13: **`42`** (`39` existing + `3` new).

## New Tables
1. `organization.QualityParameter`
   - PK `PK_QualityParameter` on `(Id)`; AK `AK_QualityParameter_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `Code` (`varchar(32)`, `Latin1_General_100_BIN2`), `Name` (`nvarchar(120)`), `Symbol` (`nvarchar(24)`), `UnitSymbol` (`nvarchar(24)` nullable), `DataType` (`tinyint`, `1..3`), `DecimalPlaces` (`tinyint`, `0..6`), `DefaultTestMethod` (`nvarchar(64)` nullable), `IsActive` (`bit`), audit columns + `RowVersion`.
   - Unique index `UX_QualityParameter_Org_Code` on `(OrganizationId, Code)`.
2. `organization.QualitySpec`
   - PK `PK_QualitySpec` on `(Id)`; AK `AK_QualitySpec_Org_Id` on `(OrganizationId, Id)`.
   - Columns: `OrganizationId`, `Code` (`varchar(32)`), `Name` (`nvarchar(120)`), `TargetKind` (`tinyint`: `1=Item`, `2=Classification`), `ItemId` (`bigint` nullable), `ClassificationId` (`bigint` nullable), `Checkpoint` (`tinyint`: `1=Incoming`, `2=Production`, `3=All`), `Version` (`int >= 1`), `IsActive` (`bit`), `Notes` (`nvarchar(1000)` nullable), audit columns + `RowVersion`.
   - FKs (`NoAction`): `(OrganizationId, ItemId) -> Item`, `(OrganizationId, ClassificationId) -> Classification`.
   - Check `CK_QualitySpec_Target`: Item requires `ItemId IS NOT NULL AND ClassificationId IS NULL`; Classification requires `ClassificationId IS NOT NULL AND ItemId IS NULL`.
   - Filtered unique active target indexes (`UX_QualitySpec_ActiveItem`, `UX_QualitySpec_ActiveClass`) + `UX_QualitySpec_Org_Code_Version`.
3. `organization.QualitySpecLine`
   - PK `PK_QualitySpecLine` on `(Id)`.
   - Columns: `OrganizationId`, `QualitySpecId`, `Sequence` (`int > 0`), `QualityParameterId` (`bigint`), `MinValue` (`decimal(18,6)` nullable), `MaxValue` (`decimal(18,6)` nullable), `TargetValue` (`decimal(18,6)` nullable), `ExpectedTextSpec` (`nvarchar(100)` nullable), `IsMandatory` (`bit`), `Notes` (`nvarchar(300)` nullable).
   - Check `CK_QualitySpecLine_Range`: `MinValue IS NULL OR MaxValue IS NULL OR MaxValue >= MinValue`.
   - FKs (`NoAction`): `(OrganizationId, QualitySpecId) -> QualitySpec`, `(OrganizationId, QualityParameterId) -> QualityParameter`.

## Extended Existing Tables
- `organization.QualityInspection`: `CertificateNumber` (`nvarchar(64)` nullable).
- `organization.QualityInspectionLine`: nullable `QualityParameterId`, `ParameterCodeSnapshot` (`varchar(32)`), `SymbolSnapshot` (`nvarchar(24)`), `UnitSymbolSnapshot` (`nvarchar(24)`), `TestMethodSnapshot` (`nvarchar(64)`), `DataType` (`tinyint`, default `2`), `MinValueSnapshot` (`decimal(18,6)`), `MaxValueSnapshot` (`decimal(18,6)`), `TargetValueSnapshot` (`decimal(18,6)`), `MeasuredNumericValue` (`decimal(18,6)`), `IsMandatory` (`bit`, default `0`).
