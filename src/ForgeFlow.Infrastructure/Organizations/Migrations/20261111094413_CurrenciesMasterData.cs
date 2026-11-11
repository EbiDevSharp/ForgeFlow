using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class CurrenciesMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "Currency",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrecisionScale = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency", x => x.Id);
                    table.UniqueConstraint("AK_Currency_Organization_Code", x => new { x.OrganizationId, x.Code });
                    table.UniqueConstraint("AK_Currency_Organization_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Currency_Code", "DATALENGTH([Code]) = 3 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_Currency_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Currency_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Currency_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Currency_Symbol", "DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0");
                    table.ForeignKey(
                        name: "FK_Currency_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_OrganizationId_CurrencyCode",
                schema: "organization",
                table: "PurchaseOrder",
                columns: new[] { "OrganizationId", "CurrencyCode" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder",
                sql: "DATALENGTH([CurrencyCode]) = 3 AND DATALENGTH([CurrencyCode]) = LEN([CurrencyCode]) AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled')))");

            migrationBuilder.CreateIndex(
                name: "IX_Currency_Organization_IsActive_Code",
                schema: "organization",
                table: "Currency",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Currency_Organization_Code",
                schema: "organization",
                table: "Currency",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO [organization].[Currency] ([OrganizationId], [Code], [Name], [Symbol], [PrecisionScale], [IsActive], [CreatedAtUtc], [CreatedByActorId])
                SELECT [o].[Id], [seed].[Code], [seed].[Name], [seed].[Symbol], [seed].[PrecisionScale], 1, SYSUTCDATETIME(), [o].[CreatedByActorId]
                FROM [organization].[Organization] AS [o]
                CROSS JOIN (VALUES
                    ('IRR', N'Iranian Rial', N'IRR', CAST(0 AS tinyint)),
                    ('USD', N'US Dollar', N'$', CAST(2 AS tinyint)),
                    ('EUR', N'Euro', N'€', CAST(2 AS tinyint))
                ) AS [seed]([Code], [Name], [Symbol], [PrecisionScale])
                WHERE NOT EXISTS (
                    SELECT 1 FROM [organization].[Currency] AS [c]
                    WHERE [c].[OrganizationId] = [o].[Id] AND [c].[Code] = [seed].[Code]
                );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder",
                columns: new[] { "OrganizationId", "CurrencyCode" },
                principalSchema: "organization",
                principalTable: "Currency",
                principalColumns: new[] { "OrganizationId", "Code" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Currency] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Currency] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Currency])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder");

            migrationBuilder.DropTable(
                name: "Currency",
                schema: "organization");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrder_OrganizationId_CurrencyCode",
                schema: "organization",
                table: "PurchaseOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrder_Currency",
                schema: "organization",
                table: "PurchaseOrder",
                sql: "DATALENGTH([CurrencyCode]) = 3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 IN ('IRR','USD','EUR')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled')))");
        }
    }
}
