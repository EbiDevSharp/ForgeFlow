using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationSitesBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "Organization",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organization", x => x.Id);
                    table.CheckConstraint("CK_Organization_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Organization_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Organization_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Site",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Site", x => x.Id);
                    table.UniqueConstraint("AK_Site_Organization_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Site_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Site_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Site_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Site_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationChange",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    EventCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationChange", x => x.Id);
                    table.CheckConstraint("CK_OrganizationChange_Actor", "[ActorId] > 0");
                    table.CheckConstraint("CK_OrganizationChange_Event", "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
                    table.CheckConstraint("CK_OrganizationChange_Json", "ISJSON([ChangesJson]) = 1");
                    table.ForeignKey(
                        name: "FK_OrganizationChange_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationChange_Site",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organization_IsActive_Code",
                schema: "organization",
                table: "Organization",
                columns: new[] { "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Organization_Code",
                schema: "organization",
                table: "Organization",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationChange_Organization_Time",
                schema: "organization",
                table: "OrganizationChange",
                columns: new[] { "OrganizationId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationChange_Site_Time",
                schema: "organization",
                table: "OrganizationChange",
                columns: new[] { "OrganizationId", "SiteId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Site_Organization_IsActive_Code",
                schema: "organization",
                table: "Site",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Site_Organization_Code",
                schema: "organization",
                table: "Site",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            // No logins/users or role assignments. Deployment must map its separate runtime identity explicitly.
            migrationBuilder.Sql("""
                CREATE ROLE [ForgeFlowOrganizationRuntime] AUTHORIZATION [dbo];
                GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Organization] TO [ForgeFlowOrganizationRuntime];
                GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Site] TO [ForgeFlowOrganizationRuntime];
                DENY DELETE ON OBJECT::[organization].[Organization] TO [ForgeFlowOrganizationRuntime];
                DENY DELETE ON OBJECT::[organization].[Site] TO [ForgeFlowOrganizationRuntime];
                GRANT SELECT, INSERT ON OBJECT::[organization].[OrganizationChange] TO [ForgeFlowOrganizationRuntime];
                DENY UPDATE, DELETE ON OBJECT::[organization].[OrganizationChange] TO [ForgeFlowOrganizationRuntime];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A populated installation requires a reviewed backup/forward recovery, never destructive downgrade.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Organization])
                   OR EXISTS (SELECT 1 FROM [organization].[Site])
                   OR EXISTS (SELECT 1 FROM [organization].[OrganizationChange])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                DROP ROLE [ForgeFlowOrganizationRuntime];
                """);
            migrationBuilder.DropTable(
                name: "OrganizationChange",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Site",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Organization",
                schema: "organization");
        }
    }
}
