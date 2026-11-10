using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class UnitsAndConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "Unit",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Dimension = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_Unit", x => x.Id);
                    table.UniqueConstraint("AK_Unit_Organization_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Unit_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Unit_Dimension", "[Dimension] BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_Unit_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Unit_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Unit_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_Unit_Symbol", "DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0");
                    table.ForeignKey(
                        name: "FK_Unit_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UnitConversion",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    FromUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ToUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitConversion", x => x.Id);
                    table.CheckConstraint("CK_UnitConversion_DifferentUnits", "[FromUnitId] <> [ToUnitId]");
                    table.CheckConstraint("CK_UnitConversion_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_UnitConversion_PositiveFactor", "[Factor] > 0");
                    table.ForeignKey(
                        name: "FK_UnitConversion_FromUnit",
                        columns: x => new { x.OrganizationId, x.FromUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_UnitConversion_ToUnit",
                        columns: x => new { x.OrganizationId, x.ToUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");

            migrationBuilder.CreateIndex(
                name: "IX_Unit_Organization_IsActive_Code",
                schema: "organization",
                table: "Unit",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Unit_Organization_Code",
                schema: "organization",
                table: "Unit",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversion_OrganizationId_ToUnitId",
                schema: "organization",
                table: "UnitConversion",
                columns: new[] { "OrganizationId", "ToUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_UnitConversion_Organization_From_To",
                schema: "organization",
                table: "UnitConversion",
                columns: new[] { "OrganizationId", "FromUnitId", "ToUnitId" },
                unique: true);

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Unit] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[UnitConversion] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Unit] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[UnitConversion] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Unit])
                   OR EXISTS (SELECT 1 FROM [organization].[UnitConversion])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "UnitConversion",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Unit",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
        }
    }
}
