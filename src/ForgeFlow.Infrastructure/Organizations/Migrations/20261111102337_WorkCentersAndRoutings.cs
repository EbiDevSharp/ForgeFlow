using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class WorkCentersAndRoutings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddColumn<string>(
                name: "RoutingCodeSnapshot",
                schema: "organization",
                table: "ProductionOrder",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<long>(
                name: "RoutingId",
                schema: "organization",
                table: "ProductionOrder",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Routing",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    BomVersionId = table.Column<long>(type: "bigint", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routing", x => x.Id);
                    table.UniqueConstraint("AK_Routing_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.UniqueConstraint("AK_Routing_Org_Site_Id", x => new { x.OrganizationId, x.SiteId, x.Id });
                    table.CheckConstraint("CK_Routing_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Routing_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Routing_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Routing_Status", "[Status] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_Routing_Version", "[VersionNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_Routing_BomVersion",
                        columns: x => new { x.OrganizationId, x.BomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_Routing_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_Routing_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Routing_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "WorkCenter",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkCenterType = table.Column<byte>(type: "tinyint", nullable: false),
                    CapacityUnitsPerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DailyAvailableHours = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    HourlyCostRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenter", x => x.Id);
                    table.UniqueConstraint("AK_WorkCenter_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.UniqueConstraint("AK_WorkCenter_Org_Site_Id", x => new { x.OrganizationId, x.SiteId, x.Id });
                    table.CheckConstraint("CK_WorkCenter_Capacity", "[CapacityUnitsPerHour] > 0 AND [DailyAvailableHours] > 0 AND [DailyAvailableHours] <= 24 AND ([HourlyCostRate] IS NULL OR [HourlyCostRate] >= 0)");
                    table.CheckConstraint("CK_WorkCenter_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_WorkCenter_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_WorkCenter_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_WorkCenter_Type", "[WorkCenterType] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_WorkCenter_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkCenter_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderOperation",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OperationCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkCenterId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedSetupMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlannedRunMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlannedQueueMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlannedWaitMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualSetupMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualRunMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CompletedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RequiresQualityCheck = table.Column<bool>(type: "bit", nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OperatorNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderOperation", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderOperation_Code", "DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ProductionOrderOperation_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ProductionOrderOperation_Name", "DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0");
                    table.CheckConstraint("CK_ProductionOrderOperation_Qty", "[CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0");
                    table.CheckConstraint("CK_ProductionOrderOperation_Seq", "[Sequence] >= 1");
                    table.CheckConstraint("CK_ProductionOrderOperation_Status", "[Status] BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_ProductionOrderOperation_Times", "[PlannedSetupMinutes] >= 0 AND [PlannedRunMinutes] >= 0 AND [PlannedQueueMinutes] >= 0 AND [PlannedWaitMinutes] >= 0 AND [ActualSetupMinutes] >= 0 AND [ActualRunMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductionOrderOperation_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderOperation_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrderOperation_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductionOrderOperation_WorkCenter",
                        columns: x => new { x.OrganizationId, x.SiteId, x.WorkCenterId },
                        principalSchema: "organization",
                        principalTable: "WorkCenter",
                        principalColumns: new[] { "OrganizationId", "SiteId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "RoutingOperation",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    RoutingId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OperationCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkCenterId = table.Column<long>(type: "bigint", nullable: false),
                    SetupMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RunMinutesPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QueueMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WaitMinutes = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequiresQualityCheck = table.Column<bool>(type: "bit", nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingOperation", x => x.Id);
                    table.CheckConstraint("CK_RoutingOperation_Code", "DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_RoutingOperation_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_RoutingOperation_Name", "DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0");
                    table.CheckConstraint("CK_RoutingOperation_Seq", "[Sequence] >= 1");
                    table.CheckConstraint("CK_RoutingOperation_Times", "[SetupMinutes] >= 0 AND [RunMinutesPerUnit] > 0 AND [QueueMinutes] >= 0 AND [WaitMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_RoutingOperation_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RoutingOperation_Routing",
                        columns: x => new { x.OrganizationId, x.SiteId, x.RoutingId },
                        principalSchema: "organization",
                        principalTable: "Routing",
                        principalColumns: new[] { "OrganizationId", "SiteId", "Id" });
                    table.ForeignKey(
                        name: "FK_RoutingOperation_WorkCenter",
                        columns: x => new { x.OrganizationId, x.SiteId, x.WorkCenterId },
                        principalSchema: "organization",
                        principalTable: "WorkCenter",
                        principalColumns: new[] { "OrganizationId", "SiteId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrder_OrganizationId_SiteId_RoutingId",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "SiteId", "RoutingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionOrder_RoutingCodeSnapshot",
                schema: "organization",
                table: "ProductionOrder",
                sql: "[RoutingCodeSnapshot] IS NULL OR (DATALENGTH([RoutingCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([RoutingCodeSnapshot]) = LEN([RoutingCodeSnapshot]) AND [RoutingCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([RoutingCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionOrder_RoutingPair",
                schema: "organization",
                table: "ProductionOrder",
                sql: "([RoutingId] IS NULL AND [RoutingCodeSnapshot] IS NULL) OR ([RoutingId] IS NOT NULL AND [RoutingCodeSnapshot] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderOperation_Org_Site_WC_Status",
                schema: "organization",
                table: "ProductionOrderOperation",
                columns: new[] { "OrganizationId", "SiteId", "WorkCenterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductionOrderOperation_Order_Seq",
                schema: "organization",
                table: "ProductionOrderOperation",
                columns: new[] { "OrganizationId", "ProductionOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routing_Org_Site_Item_Status",
                schema: "organization",
                table: "Routing",
                columns: new[] { "OrganizationId", "SiteId", "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Routing_OrganizationId_BomVersionId",
                schema: "organization",
                table: "Routing",
                columns: new[] { "OrganizationId", "BomVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Routing_OrganizationId_ItemId",
                schema: "organization",
                table: "Routing",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "UX_Routing_Org_Site_Code_Version",
                schema: "organization",
                table: "Routing",
                columns: new[] { "OrganizationId", "SiteId", "Code", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Routing_Org_Site_Item_Default",
                schema: "organization",
                table: "Routing",
                columns: new[] { "OrganizationId", "SiteId", "ItemId" },
                unique: true,
                filter: "[Status] = 2 AND [IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingOperation_Org_Site_WC",
                schema: "organization",
                table: "RoutingOperation",
                columns: new[] { "OrganizationId", "SiteId", "WorkCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingOperation_OrganizationId_SiteId_RoutingId",
                schema: "organization",
                table: "RoutingOperation",
                columns: new[] { "OrganizationId", "SiteId", "RoutingId" });

            migrationBuilder.CreateIndex(
                name: "UX_RoutingOperation_Routing_Seq",
                schema: "organization",
                table: "RoutingOperation",
                columns: new[] { "OrganizationId", "RoutingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenter_Org_Site_Active",
                schema: "organization",
                table: "WorkCenter",
                columns: new[] { "OrganizationId", "SiteId", "IsActive", "Code" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkCenter_Org_Site_Code",
                schema: "organization",
                table: "WorkCenter",
                columns: new[] { "OrganizationId", "SiteId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrder_Routing",
                schema: "organization",
                table: "ProductionOrder",
                columns: new[] { "OrganizationId", "SiteId", "RoutingId" },
                principalSchema: "organization",
                principalTable: "Routing",
                principalColumns: new[] { "OrganizationId", "SiteId", "Id" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[WorkCenter] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Routing] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[RoutingOperation] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderOperation] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[WorkCenter] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Routing] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[WorkCenter])
                   OR EXISTS (SELECT 1 FROM [organization].[Routing])
                   OR EXISTS (SELECT 1 FROM [organization].[ProductionOrderOperation])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrder_Routing",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.DropTable(
                name: "ProductionOrderOperation",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "RoutingOperation",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Routing",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "WorkCenter",
                schema: "organization");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrder_OrganizationId_SiteId_RoutingId",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionOrder_RoutingCodeSnapshot",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionOrder_RoutingPair",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropColumn(
                name: "RoutingCodeSnapshot",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.DropColumn(
                name: "RoutingId",
                schema: "organization",
                table: "ProductionOrder");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled')))");
        }
    }
}
