using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class StockReservationsAndDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance");

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedBaseQuantity",
                schema: "organization",
                table: "InventoryBalance",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "StockReservation",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderInputId = table.Column<long>(type: "bigint", nullable: true),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, defaultValue: "", collation: "Latin1_General_100_BIN2"),
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ReservedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ConsumedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservation", x => x.Id);
                    table.CheckConstraint("CK_StockReservation_LotCode", "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))");
                    table.CheckConstraint("CK_StockReservation_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_StockReservation_Qty", "[ReservedBaseQuantity] > 0 AND [ConsumedBaseQuantity] >= 0 AND [ConsumedBaseQuantity] <= [ReservedBaseQuantity]");
                    table.CheckConstraint("CK_StockReservation_Status", "[Status] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_StockReservation_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockReservation_Order",
                        columns: x => new { x.OrganizationId, x.ProductionOrderId },
                        principalSchema: "organization",
                        principalTable: "ProductionOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockReservation_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockReservation_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockReservation_Unit",
                        columns: x => new { x.OrganizationId, x.BaseUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockReservation_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance",
                sql: "[BlockedBaseQuantity] >= 0 AND [ReservedBaseQuantity] >= 0 AND ([BlockedBaseQuantity] + [ReservedBaseQuantity]) <= [OnHandBaseQuantity]");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_Org_Order_Status",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "ProductionOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_Org_Site_Wh_Item_Lot_Status",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "SiteId", "WarehouseId", "ItemId", "LotCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_OrganizationId_BaseUnitId",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_OrganizationId_ItemId",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_OrganizationId_WarehouseId",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[StockReservation] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[StockReservation] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[StockReservation])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "StockReservation",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance");

            migrationBuilder.DropColumn(
                name: "ReservedBaseQuantity",
                schema: "organization",
                table: "InventoryBalance");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBalance_Blocked",
                schema: "organization",
                table: "InventoryBalance",
                sql: "[BlockedBaseQuantity] >= 0 AND [BlockedBaseQuantity] <= [OnHandBaseQuantity]");
        }
    }
}
