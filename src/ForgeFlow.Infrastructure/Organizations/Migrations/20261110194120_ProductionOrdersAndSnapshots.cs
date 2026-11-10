using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class ProductionOrdersAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "ProductionOrder",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    OrderNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RequestedUnitId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BomId = table.Column<long>(type: "bigint", nullable: false),
                    BomCodeSnapshot = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BomVersionId = table.Column<long>(type: "bigint", nullable: false),
                    BomVersionNumberSnapshot = table.Column<int>(type: "int", nullable: false),
                    BaseOutputQuantitySnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseOutputUnitIdSnapshot = table.Column<long>(type: "bigint", nullable: false),
                    ActualProducedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ActualScrapQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrder", x => x.Id);
                    table.UniqueConstraint("AK_ProductionOrder_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_ProductionOrder_Actuals", "[ActualProducedQuantity] >= 0 AND [ActualScrapQuantity] >= 0");
                    table.CheckConstraint("CK_ProductionOrder_BaseOutputQty", "[BaseOutputQuantitySnapshot] > 0 AND [BomVersionNumberSnapshot] > 0");
                    table.CheckConstraint("CK_ProductionOrder_BomCodeSnapshot", "DATALENGTH([BomCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([BomCodeSnapshot]) = LEN([BomCodeSnapshot]) AND [BomCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([BomCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ProductionOrder_Dates", "[PlannedDueDate] >= [PlannedStartDate]");
                    table.CheckConstraint("CK_ProductionOrder_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ProductionOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ProductionOrder_RequestedQty", "[RequestedQuantity] > 0");
                    table.CheckConstraint("CK_ProductionOrder_Status", "[Status] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ProductionOrder_BaseOutputUnitSnapshot",
                        columns: x => new { x.OrganizationId, x.BaseOutputUnitIdSnapshot },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_Bom",
                        columns: x => new { x.OrganizationId, x.BomId },
                        principalSchema: "organization",
                        principalTable: "Bom",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_BomVersion",
                        columns: x => new { x.OrganizationId, x.BomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrder_RequestedUnit",
                        columns: x => new { x.OrganizationId, x.RequestedUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrder_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderContext",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    DimensionCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DimensionValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderContext", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderContext_DimCode", "DATALENGTH([DimensionCode]) BETWEEN 1 AND 32 AND DATALENGTH([DimensionCode]) = LEN([DimensionCode]) AND [DimensionCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DimensionCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ProductionOrderContext_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ProductionOrderContext_Value", "DATALENGTH([DimensionValue]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductionOrderContext_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderInput",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ParentItemId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    NetQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ScrapPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    GrossQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    IsOptional = table.Column<bool>(type: "bit", nullable: false),
                    IsSubAssembly = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedSubBomVersionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderInput", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderInput_LevelSeq", "[Level] >= 1 AND [Sequence] >= 1");
                    table.CheckConstraint("CK_ProductionOrderInput_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ProductionOrderInput_Quantities", "[NetQuantity] > 0 AND [GrossQuantity] > 0");
                    table.CheckConstraint("CK_ProductionOrderInput_Scrap", "[ScrapPercentage] >= 0 AND [ScrapPercentage] < 100");
                    table.ForeignKey(
                        name: "FK_ProductionOrderInput_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderInput_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderInput_ParentItem",
                        columns: x => new { x.OrganizationId, x.ParentItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderInput_SubBomVersion",
                        columns: x => new { x.OrganizationId, x.ResolvedSubBomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderInput_Unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderOutput",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    OutputType = table.Column<byte>(type: "tinyint", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderOutput", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderOutput_LevelSeq", "[Level] >= 1 AND [Sequence] >= 1");
                    table.CheckConstraint("CK_ProductionOrderOutput_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ProductionOrderOutput_Qty", "[PlannedQuantity] > 0");
                    table.CheckConstraint("CK_ProductionOrderOutput_Type", "[OutputType] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_ProductionOrderOutput_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderOutput_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderOutput_Unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled')))");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_Org_BomVersion",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "BomVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_Org_Site_Status_Date",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "SiteId", "Status", "PlannedStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_OrganizationId_BaseOutputUnitIdSnapshot",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "BaseOutputUnitIdSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_OrganizationId_BomId",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "BomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_OrganizationId_ItemId",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_OrganizationId_RequestedUnitId",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "RequestedUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductionOrder_Org_Number",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderContext_OrganizationId_ProductionOrderId",
                schema: "organization",
                table: "ProductionOrderContext",
                columns: new[] { "OrganizationId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductionOrderContext_Order_Dim",
                schema: "organization",
                table: "ProductionOrderContext",
                columns: new[] { "ProductionOrderId", "DimensionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderInput_Org_Order_LvlSeq",
                schema: "organization",
                table: "ProductionOrderInput",
                columns: new[] { "OrganizationId", "ProductionOrderId", "Level", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderInput_OrganizationId_ItemId",
                schema: "organization",
                table: "ProductionOrderInput",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderInput_OrganizationId_ParentItemId",
                schema: "organization",
                table: "ProductionOrderInput",
                columns: new[] { "OrganizationId", "ParentItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderInput_OrganizationId_ResolvedSubBomVersionId",
                schema: "organization",
                table: "ProductionOrderInput",
                columns: new[] { "OrganizationId", "ResolvedSubBomVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderInput_OrganizationId_UnitId",
                schema: "organization",
                table: "ProductionOrderInput",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderOutput_Org_Order_LvlSeq",
                schema: "organization",
                table: "ProductionOrderOutput",
                columns: new[] { "OrganizationId", "ProductionOrderId", "Level", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderOutput_OrganizationId_ItemId",
                schema: "organization",
                table: "ProductionOrderOutput",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderOutput_OrganizationId_UnitId",
                schema: "organization",
                table: "ProductionOrderOutput",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ProductionOrder] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderInput] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderOutput] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderContext] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[ProductionOrder] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[ProductionOrder])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "ProductionOrderContext",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ProductionOrderInput",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ProductionOrderOutput",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ProductionOrder",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
        }
    }
}
