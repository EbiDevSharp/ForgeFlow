using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class RolesAndGranularPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Role_Scope",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.AlterColumn<long>(
                name: "RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "[ScopeKind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId" },
                unique: true,
                filter: "[ScopeKind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId", "SiteId" },
                unique: true,
                filter: "[ScopeKind] = 2");

            migrationBuilder.CreateTable(
                name: "RoleDefinition",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "100, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    ScopeKind = table.Column<byte>(type: "tinyint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDefinition", x => x.Id);
                    table.CheckConstraint("CK_RoleDefinition_Invariant", "([IsSystem]=1 AND [OrganizationId] IS NULL AND [Id] IN (1,2,3) AND [IsActive]=1) OR ([IsSystem]=0 AND [OrganizationId] IS NOT NULL AND [Id] >= 100 AND [ScopeKind] IN (1,2))");
                    table.CheckConstraint("CK_RoleDefinition_Metadata", "(([IsSystem]=1 AND [CreatedByActorId]=0) OR ([IsSystem]=0 AND [CreatedByActorId]>0)) AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_RoleDefinition_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPermissionGrant",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ScopeKind = table.Column<byte>(type: "tinyint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    PermissionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionGrant", x => x.Id);
                    table.CheckConstraint("CK_UserPermissionGrant_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.CheckConstraint("CK_UserPermissionGrant_Scope", "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode]) AND (([ScopeKind]=1 AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [SiteId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_UserPermissionGrant_OrganizationMembership_UserId_OrganizationId",
                        columns: x => new { x.UserId, x.OrganizationId },
                        principalSchema: "auth",
                        principalTable: "OrganizationMembership",
                        principalColumns: new[] { "UserId", "OrganizationId" });
                    table.ForeignKey(
                        name: "FK_UserPermissionGrant_SiteMembership_UserId_OrganizationId_SiteId",
                        columns: x => new { x.UserId, x.OrganizationId, x.SiteId },
                        principalSchema: "auth",
                        principalTable: "SiteMembership",
                        principalColumns: new[] { "UserId", "OrganizationId", "SiteId" });
                    table.ForeignKey(
                        name: "FK_UserPermissionGrant_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                schema: "auth",
                columns: table => new
                {
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => new { x.RoleId, x.PermissionCode });
                    table.CheckConstraint("CK_RolePermission_Code", "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode])");
                    table.CheckConstraint("CK_RolePermission_Metadata", "[CreatedByActorId] >= 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_RolePermission_RoleDefinition_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "auth",
                        principalTable: "RoleDefinition",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "auth",
                table: "RoleDefinition",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByActorId", "IsActive", "IsSystem", "Name", "NormalizedName", "OrganizationId", "ScopeKind", "UpdatedAtUtc", "UpdatedByActorId" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, true, "OrganizationAdministrator", "ORGANIZATIONADMINISTRATOR", null, (byte)1, null, null },
                    { 2L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, true, "SiteOperator", "SITEOPERATOR", null, (byte)2, null, null },
                    { 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, true, "DeploymentProvisioner", "DEPLOYMENTPROVISIONER", null, (byte)0, null, null }
                });

            migrationBuilder.InsertData(
                schema: "auth",
                table: "RolePermission",
                columns: new[] { "PermissionCode", "RoleId", "CreatedAtUtc", "CreatedByActorId", "IsActive", "UpdatedAtUtc", "UpdatedByActorId" },
                values: new object[,]
                {
                    { "Memberships.Manage", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Organizations.Read", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Organizations.SetActive", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Organizations.Update", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Permissions.Assign", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Roles.AssignFixed", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Roles.Manage", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Roles.Read", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.Create", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.Read", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.SetActive", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.Update", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.Create", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.Read", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.ResetPassword", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.SetActive", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.Unlock", 1L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.Read", 2L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.SetActive", 2L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Sites.Update", 2L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Organizations.Create", 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.AttachOrganization", 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.ResetPassword", 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.SetActive", 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null },
                    { "Users.Unlock", 3L, new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), 0L, true, null, null }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged')");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                column: "RoleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Role_Scope",
                schema: "auth",
                table: "ScopedRoleAssignment",
                sql: "([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL) OR ([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]>=100 AND (([ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinition_OrganizationId_IsActive_ScopeKind",
                schema: "auth",
                table: "RoleDefinition",
                columns: new[] { "OrganizationId", "IsActive", "ScopeKind" });

            migrationBuilder.CreateIndex(
                name: "UX_RoleDefinition_Org_Name",
                schema: "auth",
                table: "RoleDefinition",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true,
                filter: "[IsSystem] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_PermissionCode_IsActive_RoleId",
                schema: "auth",
                table: "RolePermission",
                columns: new[] { "PermissionCode", "IsActive", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionGrant_UserId_IsActive_ScopeKind_OrganizationId_SiteId_PermissionCode",
                schema: "auth",
                table: "UserPermissionGrant",
                columns: new[] { "UserId", "IsActive", "ScopeKind", "OrganizationId", "SiteId", "PermissionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionGrant_UserId_OrganizationId_PermissionCode",
                schema: "auth",
                table: "UserPermissionGrant",
                columns: new[] { "UserId", "OrganizationId", "PermissionCode" },
                unique: true,
                filter: "[ScopeKind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionGrant_UserId_OrganizationId_SiteId_PermissionCode",
                schema: "auth",
                table: "UserPermissionGrant",
                columns: new[] { "UserId", "OrganizationId", "SiteId", "PermissionCode" },
                unique: true,
                filter: "[ScopeKind] = 2");

            migrationBuilder.AddForeignKey(
                name: "FK_ScopedRoleAssignment_RoleDefinition_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                column: "RoleId",
                principalSchema: "auth",
                principalTable: "RoleDefinition",
                principalColumn: "Id");

            migrationBuilder.Sql(
                """
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT ON OBJECT::[auth].[RoleDefinition] TO [ForgeFlowAuthRuntime];
                    GRANT SELECT ON OBJECT::[auth].[RolePermission] TO [ForgeFlowAuthRuntime];
                    GRANT SELECT ON OBJECT::[auth].[UserPermissionGrant] TO [ForgeFlowAuthRuntime];
                    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[RoleDefinition] TO [ForgeFlowAuthRuntime];
                    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[RolePermission] TO [ForgeFlowAuthRuntime];
                    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserPermissionGrant] TO [ForgeFlowAuthRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [auth].[RoleDefinition] WHERE [Id] >= 100)
                    OR EXISTS (SELECT 1 FROM [auth].[UserPermissionGrant])
                    THROW 50003, 'Auth.NonEmptyRollbackDenied', 1;
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[RoleDefinition] FROM [ForgeFlowAuthRuntime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[RolePermission] FROM [ForgeFlowAuthRuntime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserPermissionGrant] FROM [ForgeFlowAuthRuntime];
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ScopedRoleAssignment_RoleDefinition_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropTable(
                name: "RolePermission",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserPermissionGrant",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "RoleDefinition",
                schema: "auth");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Role_Scope",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment");

            migrationBuilder.AlterColumn<byte>(
                name: "RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "[ScopeKind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId" },
                unique: true,
                filter: "[ScopeKind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId", "SiteId" },
                unique: true,
                filter: "[ScopeKind] = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','CulturePreferenceChanged')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Role_Scope",
                schema: "auth",
                table: "ScopedRoleAssignment",
                sql: "([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL)");
        }
    }
}
