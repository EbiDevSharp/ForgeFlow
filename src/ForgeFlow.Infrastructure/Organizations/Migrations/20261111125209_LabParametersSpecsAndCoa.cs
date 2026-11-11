using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class LabParametersSpecsAndCoa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddColumn<byte>(
                name: "DataType",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)2);

            migrationBuilder.AddColumn<bool>(
                name: "IsMandatory",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeasuredNumericValue",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterCodeSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<long>(
                name: "QualityParameterId",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymbolSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestMethodSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitSymbolSnapshot",
                schema: "organization",
                table: "QualityInspectionLine",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateNumber",
                schema: "organization",
                table: "QualityInspection",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QualityParameter",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    UnitSymbol = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    DataType = table.Column<byte>(type: "tinyint", nullable: false),
                    DecimalPlaces = table.Column<byte>(type: "tinyint", nullable: false),
                    DefaultTestMethod = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityParameter", x => x.Id);
                    table.UniqueConstraint("AK_QualityParameter_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_QualityParameter_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_QualityParameter_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_QualityParameter_Name", "DATALENGTH([Name]) BETWEEN 2 AND 240 AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_QualityParameter_Symbol", "DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0");
                    table.CheckConstraint("CK_QualityParameter_Type", "[DataType] IN (1,2,3) AND [DecimalPlaces] BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_QualityParameter_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QualitySpec",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetKind = table.Column<byte>(type: "tinyint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: true),
                    ClassificationId = table.Column<long>(type: "bigint", nullable: true),
                    Checkpoint = table.Column<byte>(type: "tinyint", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualitySpec", x => x.Id);
                    table.UniqueConstraint("AK_QualitySpec_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_QualitySpec_Checkpoint", "[Checkpoint] IN (1,2,3)");
                    table.CheckConstraint("CK_QualitySpec_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_QualitySpec_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_QualitySpec_Target", "([TargetKind] = 1 AND [ItemId] IS NOT NULL AND [ClassificationId] IS NULL) OR ([TargetKind] = 2 AND [ClassificationId] IS NOT NULL AND [ItemId] IS NULL)");
                    table.CheckConstraint("CK_QualitySpec_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_QualitySpec_Classification",
                        columns: x => new { x.OrganizationId, x.ClassificationId },
                        principalSchema: "organization",
                        principalTable: "Classification",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualitySpec_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualitySpec_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QualitySpecLine",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    QualitySpecId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    QualityParameterId = table.Column<long>(type: "bigint", nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    TargetValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ExpectedTextSpec = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualitySpecLine", x => x.Id);
                    table.CheckConstraint("CK_QualitySpecLine_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_QualitySpecLine_Range", "[MinValue] IS NULL OR [MaxValue] IS NULL OR [MaxValue] >= [MinValue]");
                    table.CheckConstraint("CK_QualitySpecLine_Seq", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_QualitySpecLine_Param",
                        columns: x => new { x.OrganizationId, x.QualityParameterId },
                        principalSchema: "organization",
                        principalTable: "QualityParameter",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualitySpecLine_Spec",
                        columns: x => new { x.OrganizationId, x.QualitySpecId },
                        principalSchema: "organization",
                        principalTable: "QualitySpec",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspectionLine_OrganizationId_QualityParameterId",
                schema: "organization",
                table: "QualityInspectionLine",
                columns: new[] { "OrganizationId", "QualityParameterId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated','QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");

            migrationBuilder.CreateIndex(
                name: "UX_QualityParameter_Org_Code",
                schema: "organization",
                table: "QualityParameter",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QualitySpec_ActiveClass",
                schema: "organization",
                table: "QualitySpec",
                columns: new[] { "OrganizationId", "ClassificationId", "Checkpoint" },
                unique: true,
                filter: "[IsActive] = 1 AND [TargetKind] = 2");

            migrationBuilder.CreateIndex(
                name: "UX_QualitySpec_ActiveItem",
                schema: "organization",
                table: "QualitySpec",
                columns: new[] { "OrganizationId", "ItemId", "Checkpoint" },
                unique: true,
                filter: "[IsActive] = 1 AND [TargetKind] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_QualitySpec_Org_Code_Version",
                schema: "organization",
                table: "QualitySpec",
                columns: new[] { "OrganizationId", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualitySpecLine_OrganizationId_QualityParameterId",
                schema: "organization",
                table: "QualitySpecLine",
                columns: new[] { "OrganizationId", "QualityParameterId" });

            migrationBuilder.CreateIndex(
                name: "UX_QualitySpecLine_Spec_Param",
                schema: "organization",
                table: "QualitySpecLine",
                columns: new[] { "OrganizationId", "QualitySpecId", "QualityParameterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QualitySpecLine_Spec_Seq",
                schema: "organization",
                table: "QualitySpecLine",
                columns: new[] { "OrganizationId", "QualitySpecId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QualityInspectionLine_Parameter",
                schema: "organization",
                table: "QualityInspectionLine",
                columns: new[] { "OrganizationId", "QualityParameterId" },
                principalSchema: "organization",
                principalTable: "QualityParameter",
                principalColumns: new[] { "OrganizationId", "Id" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[QualityParameter] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[QualitySpec] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[QualitySpecLine] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[QualityParameter] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[QualitySpec] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[QualityParameter])
                   OR EXISTS (SELECT 1 FROM [organization].[QualitySpec])
                   OR EXISTS (SELECT 1 FROM [organization].[QualitySpecLine])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_QualityInspectionLine_Parameter",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropTable(
                name: "QualitySpecLine",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "QualityParameter",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "QualitySpec",
                schema: "organization");

            migrationBuilder.DropIndex(
                name: "IX_QualityInspectionLine_OrganizationId_QualityParameterId",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropColumn(
                name: "DataType",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "IsMandatory",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "MaxValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "MeasuredNumericValue",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "MinValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "ParameterCodeSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "QualityParameterId",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "SymbolSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "TargetValueSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "TestMethodSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "UnitSymbolSnapshot",
                schema: "organization",
                table: "QualityInspectionLine");

            migrationBuilder.DropColumn(
                name: "CertificateNumber",
                schema: "organization",
                table: "QualityInspection");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");
        }
    }
}
