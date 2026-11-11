using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class QualityControlAndLotTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.AddColumn<byte>(
                name: "QualityControlMode",
                schema: "organization",
                table: "Item",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresLotTracking",
                schema: "organization",
                table: "Item",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "QualityInspectionId",
                schema: "organization",
                table: "InventoryTransaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BlockedBaseQuantity",
                schema: "organization",
                table: "InventoryBalance",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ItemLot",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ManufactureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemLot", x => x.Id);
                    table.CheckConstraint("CK_ItemLot_Dates", "[ManufactureDate] IS NULL OR [ExpirationDate] IS NULL OR [ExpirationDate] >= [ManufactureDate]");
                    table.CheckConstraint("CK_ItemLot_LotCode", "DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ItemLot_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_ItemLot_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ItemLot_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QualityInspection",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, defaultValue: "", collation: "Latin1_General_100_BIN2"),
                    InspectionNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: true),
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    InspectedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AcceptedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RejectedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RemainingBlockedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DefectReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InspectedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    InspectedByActorId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityInspection", x => x.Id);
                    table.UniqueConstraint("AK_QualityInspection_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_QualityInspection_LotCode", "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))");
                    table.CheckConstraint("CK_QualityInspection_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_QualityInspection_Number", "DATALENGTH([InspectionNumber]) BETWEEN 1 AND 32 AND DATALENGTH([InspectionNumber]) = LEN([InspectionNumber]) AND [InspectionNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([InspectionNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_QualityInspection_Quantities", "[InspectedBaseQuantity] > 0 AND [AcceptedBaseQuantity] >= 0 AND [RejectedBaseQuantity] >= 0 AND [RemainingBlockedBaseQuantity] >= 0 AND ([AcceptedBaseQuantity] + [RejectedBaseQuantity] + [RemainingBlockedBaseQuantity]) <= [InspectedBaseQuantity]");
                    table.CheckConstraint("CK_QualityInspection_SourceType", "[SourceType] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_QualityInspection_Status", "[Status] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_QualityInspection_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualityInspection_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualityInspection_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualityInspection_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QualityInspection_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualityInspection_SourceTxn",
                        columns: x => new { x.OrganizationId, x.SourceTransactionId },
                        principalSchema: "organization",
                        principalTable: "InventoryTransaction",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_QualityInspection_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "QualityInspectionLine",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpectedSpec = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MeasuredValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityInspectionLine", x => x.Id);
                    table.CheckConstraint("CK_QualityInspectionLine_LineNum", "[LineNumber] > 0");
                    table.CheckConstraint("CK_QualityInspectionLine_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_QualityInspectionLine_Param", "DATALENGTH(LTRIM(RTRIM([ParameterName]))) > 0");
                    table.ForeignKey(
                        name: "FK_QualityInspectionLine_Inspection",
                        columns: x => new { x.OrganizationId, x.QualityInspectionId },
                        principalSchema: "organization",
                        principalTable: "QualityInspection",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Item_QualityControlMode",
                schema: "organization",
                table: "Item",
                sql: "[QualityControlMode] BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_OrganizationId_QualityInspectionId",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "QualityInspectionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction",
                sql: "[TransactionType] BETWEEN 1 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance",
                sql: "[BlockedBaseQuantity] >= 0 AND [BlockedBaseQuantity] <= [OnHandBaseQuantity]");

            migrationBuilder.CreateIndex(
                name: "IX_ItemLot_Org_Expiration",
                schema: "organization",
                table: "ItemLot",
                columns: new[] { "OrganizationId", "ExpirationDate" });

            migrationBuilder.CreateIndex(
                name: "UX_ItemLot_Org_Item_Lot",
                schema: "organization",
                table: "ItemLot",
                columns: new[] { "OrganizationId", "ItemId", "LotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_Org_Item_Lot",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "ItemId", "LotCode" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_Org_Site_Status",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "SiteId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_OrganizationId_BaseUnitId",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_OrganizationId_ProductionOrderId",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_OrganizationId_SourceTransactionId",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "SourceTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_QualityInspection_OrganizationId_WarehouseId",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "UX_QualityInspection_Org_Number",
                schema: "organization",
                table: "QualityInspection",
                columns: new[] { "OrganizationId", "InspectionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QualityInspectionLine_Org_Insp_Line",
                schema: "organization",
                table: "QualityInspectionLine",
                columns: new[] { "OrganizationId", "QualityInspectionId", "LineNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransaction_QualityInspection",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "QualityInspectionId" },
                principalSchema: "organization",
                principalTable: "QualityInspection",
                principalColumns: new[] { "OrganizationId", "Id" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ItemLot] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[QualityInspection] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[QualityInspectionLine] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[ItemLot] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[QualityInspection] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[ItemLot])
                   OR EXISTS (SELECT 1 FROM [organization].[QualityInspection])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransaction_QualityInspection",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropTable(
                name: "ItemLot",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "QualityInspectionLine",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "QualityInspection",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Item_QualityControlMode",
                schema: "organization",
                table: "Item");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransaction_OrganizationId_QualityInspectionId",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance");

            migrationBuilder.DropColumn(
                name: "QualityControlMode",
                schema: "organization",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "RequiresLotTracking",
                schema: "organization",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "QualityInspectionId",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropColumn(
                name: "BlockedBaseQuantity",
                schema: "organization",
                table: "InventoryBalance");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction",
                sql: "[TransactionType] BETWEEN 1 AND 8");
        }
    }
}
