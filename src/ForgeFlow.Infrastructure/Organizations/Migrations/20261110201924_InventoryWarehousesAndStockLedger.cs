using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class InventoryWarehousesAndStockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "Warehouse",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDefaultForSite = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouse", x => x.Id);
                    table.UniqueConstraint("AK_Warehouse_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Warehouse_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Warehouse_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Warehouse_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Warehouse_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Warehouse_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalance",
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
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    OnHandBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    LastMovedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalance", x => x.Id);
                    table.CheckConstraint("CK_InventoryBalance_LotCode", "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))");
                    table.CheckConstraint("CK_InventoryBalance_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_InventoryBalance_NonNegative", "[OnHandBaseQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryBalance_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryBalance_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryBalance_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryBalance_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransaction",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    DestinationWarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TransactionType = table.Column<byte>(type: "tinyint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: true),
                    ReversedTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    ReferenceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransaction", x => x.Id);
                    table.UniqueConstraint("AK_InventoryTransaction_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_InventoryTransaction_DocNum", "DATALENGTH([DocumentNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DocumentNumber]) = LEN([DocumentNumber]) AND [DocumentNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DocumentNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_InventoryTransaction_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_InventoryTransaction_Type", "[TransactionType] BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_DestWarehouse",
                        columns: x => new { x.OrganizationId, x.DestinationWarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_Reversal",
                        columns: x => new { x.OrganizationId, x.ReversedTransactionId },
                        principalSchema: "organization",
                        principalTable: "InventoryTransaction",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryTransaction_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "InventoryLedgerEntry",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    InventoryTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, defaultValue: "", collation: "Latin1_General_100_BIN2"),
                    EnteredQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EnteredUnitId = table.Column<long>(type: "bigint", nullable: false),
                    BaseQuantityDelta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLedgerEntry", x => x.Id);
                    table.CheckConstraint("CK_InventoryLedgerEntry_LotCode", "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))");
                    table.CheckConstraint("CK_InventoryLedgerEntry_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_InventoryLedgerEntry_Qty", "[EnteredQuantity] <> 0 AND [BaseQuantityDelta] <> 0");
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_EnteredUnit",
                        columns: x => new { x.OrganizationId, x.EnteredUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_Txn",
                        columns: x => new { x.OrganizationId, x.InventoryTransactionId },
                        principalSchema: "organization",
                        principalTable: "InventoryTransaction",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntry_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed')))");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalance_OrganizationId_BaseUnitId",
                schema: "organization",
                table: "InventoryBalance",
                columns: new[] { "OrganizationId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalance_OrganizationId_ItemId",
                schema: "organization",
                table: "InventoryBalance",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalance_OrganizationId_SiteId",
                schema: "organization",
                table: "InventoryBalance",
                columns: new[] { "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryBalance_Org_Wh_Item_Lot",
                schema: "organization",
                table: "InventoryBalance",
                columns: new[] { "OrganizationId", "WarehouseId", "ItemId", "LotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntry_Org_Wh_Item_Lot",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "WarehouseId", "ItemId", "LotCode", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntry_OrganizationId_BaseUnitId",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntry_OrganizationId_EnteredUnitId",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "EnteredUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntry_OrganizationId_ItemId",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntry_OrganizationId_SiteId",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryLedgerEntry_Org_Txn_Line",
                schema: "organization",
                table: "InventoryLedgerEntry",
                columns: new[] { "OrganizationId", "InventoryTransactionId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_Org_Site_Date",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "SiteId", "PostedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_OrganizationId_DestinationWarehouseId",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "DestinationWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_OrganizationId_ProductionOrderId",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_OrganizationId_WarehouseId",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryTransaction_Org_DocNum",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryTransaction_Org_Idempotency",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryTransaction_Org_Reversal",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "ReversedTransactionId" },
                unique: true,
                filter: "[ReversedTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Warehouse_Org_Code",
                schema: "organization",
                table: "Warehouse",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Warehouse_Org_Site_Default",
                schema: "organization",
                table: "Warehouse",
                columns: new[] { "OrganizationId", "SiteId" },
                unique: true,
                filter: "[IsDefaultForSite] = 1 AND [IsActive] = 1");

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Warehouse] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[InventoryBalance] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT ON OBJECT::[organization].[InventoryTransaction] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT ON OBJECT::[organization].[InventoryLedgerEntry] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Warehouse] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[InventoryBalance] TO [ForgeFlowOrganizationRuntime];
                    DENY UPDATE, DELETE ON OBJECT::[organization].[InventoryTransaction] TO [ForgeFlowOrganizationRuntime];
                    DENY UPDATE, DELETE ON OBJECT::[organization].[InventoryLedgerEntry] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Warehouse])
                   OR EXISTS (SELECT 1 FROM [organization].[InventoryTransaction])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "InventoryBalance",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "InventoryLedgerEntry",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "InventoryTransaction",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Warehouse",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled')))");
        }
    }
}
