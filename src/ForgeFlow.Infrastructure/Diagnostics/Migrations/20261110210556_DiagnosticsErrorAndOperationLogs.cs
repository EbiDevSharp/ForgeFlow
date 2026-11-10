using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Diagnostics.Migrations
{
    /// <inheritdoc />
    public partial class DiagnosticsErrorAndOperationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "diag");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "ErrorLog",
                schema: "diag",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErrorReference = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    ExceptionType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    SqlErrorNumber = table.Column<int>(type: "int", nullable: true),
                    RedactedMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RedactedStackTrace = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestPath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ComponentName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLog", x => x.Id);
                    table.CheckConstraint("CK_ErrorLog_Ref", "LEN([ErrorReference]) > 0 AND DATALENGTH([ErrorReference]) = LEN([ErrorReference])");
                    table.CheckConstraint("CK_ErrorLog_Scope", "[SiteId] IS NULL OR [OrganizationId] IS NOT NULL");
                    table.CheckConstraint("CK_ErrorLog_Severity", "[Severity] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_ErrorLog_Source", "[Source] BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "UserOperationLog",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    OperationCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    TargetEntityType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    TargetEntityRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOperationLog", x => x.Id);
                    table.CheckConstraint("CK_UserOperationLog_Code", "LEN([OperationCode]) > 0 AND DATALENGTH([OperationCode]) = LEN([OperationCode])");
                    table.CheckConstraint("CK_UserOperationLog_Json", "ISJSON([MetadataJson]) = 1");
                    table.CheckConstraint("CK_UserOperationLog_Outcome", "[Outcome] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_UserOperationLog_Scope", "[SiteId] IS NULL OR [OrganizationId] IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLog_CorrelationId",
                schema: "diag",
                table: "ErrorLog",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLog_OccurredAt_Id",
                schema: "diag",
                table: "ErrorLog",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLog_Severity_Time",
                schema: "diag",
                table: "ErrorLog",
                columns: new[] { "Severity", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ErrorLog_ErrorReference",
                schema: "diag",
                table: "ErrorLog",
                column: "ErrorReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationLog_Actor_Time",
                schema: "ops",
                table: "UserOperationLog",
                columns: new[] { "ActorUserId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationLog_CorrelationId",
                schema: "ops",
                table: "UserOperationLog",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationLog_OccurredAt_Id",
                schema: "ops",
                table: "UserOperationLog",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserOperationLog_Org_Site_Time",
                schema: "ops",
                table: "UserOperationLog",
                columns: new[] { "OrganizationId", "SiteId", "OccurredAtUtc", "Id" });

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, DELETE ON OBJECT::[diag].[ErrorLog] TO [ForgeFlowAuthRuntime];
                    DENY UPDATE ON OBJECT::[diag].[ErrorLog] TO [ForgeFlowAuthRuntime];
                    GRANT SELECT, INSERT ON OBJECT::[ops].[UserOperationLog] TO [ForgeFlowAuthRuntime];
                    DENY UPDATE, DELETE ON OBJECT::[ops].[UserOperationLog] TO [ForgeFlowAuthRuntime];
                END;
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, DELETE ON OBJECT::[diag].[ErrorLog] TO [ForgeFlowOrganizationRuntime];
                    DENY UPDATE ON OBJECT::[diag].[ErrorLog] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT ON OBJECT::[ops].[UserOperationLog] TO [ForgeFlowOrganizationRuntime];
                    DENY UPDATE, DELETE ON OBJECT::[ops].[UserOperationLog] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [diag].[ErrorLog])
                   OR EXISTS (SELECT 1 FROM [ops].[UserOperationLog])
                    THROW 51001, 'DiagnosticErrors.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "ErrorLog",
                schema: "diag");

            migrationBuilder.DropTable(
                name: "UserOperationLog",
                schema: "ops");
        }
    }
}
