using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class BusinessPartnersAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentSequence_Type",
                schema: "organization",
                table: "DocumentSequence");

            migrationBuilder.CreateTable(
                name: "BusinessPartner",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PersonType = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NationalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NationalLegalId = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    EconomicCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    Mobile = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    Phone = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartner", x => x.Id);
                    table.UniqueConstraint("AK_BusinessPartner_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_BusinessPartner_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_BusinessPartner_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BusinessPartner_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_BusinessPartner_PersonType", "[PersonType] BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "FK_BusinessPartner_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BusinessPartnerRoleDefinition",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartnerRoleDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPartnerRoleAssignment",
                schema: "organization",
                columns: table => new
                {
                    BusinessPartnerId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartnerRoleAssignment", x => new { x.BusinessPartnerId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_BusinessPartnerRoleAssignment_BusinessPartner",
                        column: x => x.BusinessPartnerId,
                        principalSchema: "organization",
                        principalTable: "BusinessPartner",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BusinessPartnerRoleAssignment_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BusinessPartnerRoleAssignment_RoleDefinition",
                        column: x => x.RoleId,
                        principalSchema: "organization",
                        principalTable: "BusinessPartnerRoleDefinition",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "organization",
                table: "BusinessPartnerRoleDefinition",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "IsSystem", "Name" },
                values: new object[,]
                {
                    { 1, "SUPPLIER", 1, true, true, "Supplier" },
                    { 2, "CUSTOMER", 2, true, true, "Customer" },
                    { 3, "CONTRACTOR", 3, true, true, "Contractor" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated','QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated','Customer.Created','Customer.Updated','Customer.Activated','Customer.Deactivated','BusinessPartner.Created','BusinessPartner.Updated','BusinessPartner.Activated','BusinessPartner.Deactivated','DocumentSequence.Created','DocumentSequence.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped','SalesOrder.Created','SalesOrder.Updated','SalesOrder.Approved','SalesOrder.Dispatched','SalesOrder.Closed','SalesOrder.Cancelled','DeliveryOrder.Posted','DeliveryOrder.Reversed')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentSequence_Type",
                schema: "organization",
                table: "DocumentSequence",
                sql: "[DocumentType] BETWEEN 1 AND 10");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartner_Org_IsActive_Code",
                schema: "organization",
                table: "BusinessPartner",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartner_Org_Code",
                schema: "organization",
                table: "BusinessPartner",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartnerRoleAssignment_OrganizationId",
                schema: "organization",
                table: "BusinessPartnerRoleAssignment",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartnerRoleAssignment_RoleId",
                schema: "organization",
                table: "BusinessPartnerRoleAssignment",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessPartnerRoleAssignment",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BusinessPartner",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BusinessPartnerRoleDefinition",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentSequence_Type",
                schema: "organization",
                table: "DocumentSequence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated','QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated','Customer.Created','Customer.Updated','Customer.Activated','Customer.Deactivated','DocumentSequence.Created','DocumentSequence.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped','SalesOrder.Created','SalesOrder.Updated','SalesOrder.Approved','SalesOrder.Dispatched','SalesOrder.Closed','SalesOrder.Cancelled','DeliveryOrder.Posted','DeliveryOrder.Reversed')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentSequence_Type",
                schema: "organization",
                table: "DocumentSequence",
                sql: "[DocumentType] BETWEEN 1 AND 9");
        }
    }
}
