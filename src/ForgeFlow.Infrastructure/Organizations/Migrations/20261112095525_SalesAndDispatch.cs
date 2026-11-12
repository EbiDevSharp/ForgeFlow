using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class SalesAndDispatch : Migration
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

            migrationBuilder.AlterColumn<long>(
                name: "ProductionOrderId",
                schema: "organization",
                table: "StockReservation",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "SalesOrderId",
                schema: "organization",
                table: "StockReservation",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SalesOrderLineId",
                schema: "organization",
                table: "StockReservation",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SourceKind",
                schema: "organization",
                table: "StockReservation",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<long>(
                name: "DeliveryOrderId",
                schema: "organization",
                table: "InventoryTransaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Customer",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Phone = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ShippingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.UniqueConstraint("AK_Customer_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Customer_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Customer_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Customer_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Customer_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesOrder",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    OrderNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ShippingAddressSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrder", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrder_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_SalesOrder_Currency", "DATALENGTH([CurrencyCode]) = 3 AND DATALENGTH([CurrencyCode]) = LEN([CurrencyCode]) AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_SalesOrder_Dates", "[RequestedDeliveryDate] IS NULL OR [RequestedDeliveryDate] >= [OrderDate]");
                    table.CheckConstraint("CK_SalesOrder_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_SalesOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_SalesOrder_Status", "[Status] BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_SalesOrder_Currency",
                        columns: x => new { x.OrganizationId, x.CurrencyCode },
                        principalSchema: "organization",
                        principalTable: "Currency",
                        principalColumns: new[] { "OrganizationId", "Code" });
                    table.ForeignKey(
                        name: "FK_SalesOrder_Customer",
                        columns: x => new { x.OrganizationId, x.CustomerId },
                        principalSchema: "organization",
                        principalTable: "Customer",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SalesOrder_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesOrder_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SalesOrder_SourceWarehouse",
                        columns: x => new { x.OrganizationId, x.SourceWarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "DeliveryOrder",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    DispatchNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DispatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CarrierName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOrder", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryOrder_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_DeliveryOrder_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_DeliveryOrder_Number", "DATALENGTH([DispatchNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DispatchNumber]) = LEN([DispatchNumber]) AND [DispatchNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DispatchNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_DeliveryOrder_Status", "[Status] IN (1,2)");
                    table.ForeignKey(
                        name: "FK_DeliveryOrder_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryOrder_SalesOrder",
                        columns: x => new { x.OrganizationId, x.SalesOrderId },
                        principalSchema: "organization",
                        principalTable: "SalesOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrder_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrder_Warehouse",
                        columns: x => new { x.OrganizationId, x.WarehouseId },
                        principalSchema: "organization",
                        principalTable: "Warehouse",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLine",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OrderedUnitId = table.Column<long>(type: "bigint", nullable: false),
                    BaseUnitIdSnapshot = table.Column<long>(type: "bigint", nullable: false),
                    OrderedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DispatchedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLine", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrderLine_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_SalesOrderLine_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_SalesOrderLine_Num", "[LineNumber] > 0");
                    table.CheckConstraint("CK_SalesOrderLine_Price", "[UnitPrice] IS NULL OR [UnitPrice] >= 0");
                    table.CheckConstraint("CK_SalesOrderLine_Qty", "[OrderedQuantity] > 0 AND [OrderedBaseQuantity] > 0 AND [DispatchedBaseQuantity] >= 0 AND [DispatchedBaseQuantity] <= [OrderedBaseQuantity]");
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitIdSnapshot },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Order",
                        columns: x => new { x.OrganizationId, x.SalesOrderId },
                        principalSchema: "organization",
                        principalTable: "SalesOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_OrderedUnit",
                        columns: x => new { x.OrganizationId, x.OrderedUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "DeliveryOrderLine",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    DeliveryOrderId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderLineId = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    LotCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DispatchedBaseQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseUnitIdSnapshot = table.Column<long>(type: "bigint", nullable: false),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: true),
                    CertificateNumberSnapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOrderLine", x => x.Id);
                    table.CheckConstraint("CK_DeliveryOrderLine_LotCode", "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))");
                    table.CheckConstraint("CK_DeliveryOrderLine_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_DeliveryOrderLine_Num", "[LineNumber] > 0");
                    table.CheckConstraint("CK_DeliveryOrderLine_Qty", "[DispatchedBaseQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLine_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitIdSnapshot },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLine_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLine_Order",
                        columns: x => new { x.OrganizationId, x.DeliveryOrderId },
                        principalSchema: "organization",
                        principalTable: "DeliveryOrder",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLine_QualityInspection",
                        columns: x => new { x.OrganizationId, x.QualityInspectionId },
                        principalSchema: "organization",
                        principalTable: "QualityInspection",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLine_SalesOrderLine",
                        columns: x => new { x.OrganizationId, x.SalesOrderLineId },
                        principalSchema: "organization",
                        principalTable: "SalesOrderLine",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_Org_SalesOrder_Status",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_OrganizationId_SalesOrderLineId",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "SalesOrderLineId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservation_Source",
                schema: "organization",
                table: "StockReservation",
                sql: "([SourceKind] = 1 AND [ProductionOrderId] IS NOT NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL) OR ([SourceKind] = 2 AND [SalesOrderId] IS NOT NULL AND [ProductionOrderId] IS NULL AND [ProductionOrderInputId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated','QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated','Customer.Created','Customer.Updated','Customer.Activated','Customer.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped','SalesOrder.Created','SalesOrder.Updated','SalesOrder.Approved','SalesOrder.Dispatched','SalesOrder.Closed','SalesOrder.Cancelled','DeliveryOrder.Posted','DeliveryOrder.Reversed')))");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransaction_Org_DeliveryOrder",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "DeliveryOrderId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction",
                sql: "[TransactionType] BETWEEN 1 AND 10");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Org_IsActive_Code",
                schema: "organization",
                table: "Customer",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Customer_Org_Code",
                schema: "organization",
                table: "Customer",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrder_Org_Site_Order",
                schema: "organization",
                table: "DeliveryOrder",
                columns: new[] { "OrganizationId", "SiteId", "SalesOrderId", "DispatchDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrder_OrganizationId_SalesOrderId",
                schema: "organization",
                table: "DeliveryOrder",
                columns: new[] { "OrganizationId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrder_OrganizationId_WarehouseId",
                schema: "organization",
                table: "DeliveryOrder",
                columns: new[] { "OrganizationId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryOrder_Org_Number",
                schema: "organization",
                table: "DeliveryOrder",
                columns: new[] { "OrganizationId", "DispatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLine_Org_Item_Lot",
                schema: "organization",
                table: "DeliveryOrderLine",
                columns: new[] { "OrganizationId", "ItemId", "LotCode" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLine_OrganizationId_BaseUnitIdSnapshot",
                schema: "organization",
                table: "DeliveryOrderLine",
                columns: new[] { "OrganizationId", "BaseUnitIdSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLine_OrganizationId_QualityInspectionId",
                schema: "organization",
                table: "DeliveryOrderLine",
                columns: new[] { "OrganizationId", "QualityInspectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLine_OrganizationId_SalesOrderLineId",
                schema: "organization",
                table: "DeliveryOrderLine",
                columns: new[] { "OrganizationId", "SalesOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryOrderLine_Org_Order_Line",
                schema: "organization",
                table: "DeliveryOrderLine",
                columns: new[] { "OrganizationId", "DeliveryOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_Org_Site_Status",
                schema: "organization",
                table: "SalesOrder",
                columns: new[] { "OrganizationId", "SiteId", "Status", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_OrganizationId_CurrencyCode",
                schema: "organization",
                table: "SalesOrder",
                columns: new[] { "OrganizationId", "CurrencyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_OrganizationId_CustomerId",
                schema: "organization",
                table: "SalesOrder",
                columns: new[] { "OrganizationId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_OrganizationId_SourceWarehouseId",
                schema: "organization",
                table: "SalesOrder",
                columns: new[] { "OrganizationId", "SourceWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrder_Org_Number",
                schema: "organization",
                table: "SalesOrder",
                columns: new[] { "OrganizationId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_Org_Item",
                schema: "organization",
                table: "SalesOrderLine",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_OrganizationId_BaseUnitIdSnapshot",
                schema: "organization",
                table: "SalesOrderLine",
                columns: new[] { "OrganizationId", "BaseUnitIdSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_OrganizationId_OrderedUnitId",
                schema: "organization",
                table: "SalesOrderLine",
                columns: new[] { "OrganizationId", "OrderedUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrderLine_Org_Order_Line",
                schema: "organization",
                table: "SalesOrderLine",
                columns: new[] { "OrganizationId", "SalesOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransaction_DeliveryOrder",
                schema: "organization",
                table: "InventoryTransaction",
                columns: new[] { "OrganizationId", "DeliveryOrderId" },
                principalSchema: "organization",
                principalTable: "DeliveryOrder",
                principalColumns: new[] { "OrganizationId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservation_SalesOrder",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "SalesOrderId" },
                principalSchema: "organization",
                principalTable: "SalesOrder",
                principalColumns: new[] { "OrganizationId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservation_SalesOrderLine",
                schema: "organization",
                table: "StockReservation",
                columns: new[] { "OrganizationId", "SalesOrderLineId" },
                principalSchema: "organization",
                principalTable: "SalesOrderLine",
                principalColumns: new[] { "OrganizationId", "Id" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Customer] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[SalesOrder] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[SalesOrderLine] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[DeliveryOrder] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT ON OBJECT::[organization].[DeliveryOrderLine] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Customer] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[SalesOrder] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[DeliveryOrder] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[DeliveryOrderLine] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransaction_DeliveryOrder",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_StockReservation_SalesOrder",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropForeignKey(
                name: "FK_StockReservation_SalesOrderLine",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropTable(
                name: "DeliveryOrderLine",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "DeliveryOrder",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "SalesOrderLine",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "SalesOrder",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Customer",
                schema: "organization");

            migrationBuilder.DropIndex(
                name: "IX_StockReservation_Org_SalesOrder_Status",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropIndex(
                name: "IX_StockReservation_OrganizationId_SalesOrderLineId",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservation_Source",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransaction_Org_DeliveryOrder",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                schema: "organization",
                table: "StockReservation");

            migrationBuilder.DropColumn(
                name: "DeliveryOrderId",
                schema: "organization",
                table: "InventoryTransaction");

            migrationBuilder.AlterColumn<long>(
                name: "ProductionOrderId",
                schema: "organization",
                table: "StockReservation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated','QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransaction_Type",
                schema: "organization",
                table: "InventoryTransaction",
                sql: "[TransactionType] BETWEEN 1 AND 9");
        }
    }
}
