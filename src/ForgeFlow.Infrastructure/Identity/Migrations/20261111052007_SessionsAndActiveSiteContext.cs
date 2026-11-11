using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SessionsAndActiveSiteContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.AddColumn<long>(
                name: "ActiveOrganizationId",
                schema: "auth",
                table: "UserPreference",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ActiveSiteId",
                schema: "auth",
                table: "UserPreference",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceSummary",
                schema: "auth",
                table: "Session",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                schema: "auth",
                table: "Session",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMobileDevice",
                schema: "auth",
                table: "Session",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAtUtc",
                schema: "auth",
                table: "Session",
                type: "datetime2(7)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_UserPreference_ActiveOrganizationId_ActiveSiteId",
                schema: "auth",
                table: "UserPreference",
                columns: new[] { "ActiveOrganizationId", "ActiveSiteId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserPreference_ActiveScope",
                schema: "auth",
                table: "UserPreference",
                sql: "([ActiveSiteId] IS NULL OR [ActiveOrganizationId] IS NOT NULL)");

            migrationBuilder.Sql("UPDATE [auth].[Session] SET [LastActivityAtUtc] = [IssuedAtUtc];");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session",
                sql: "[ExpiresAtUtc] > [IssuedAtUtc] AND [LastActivityAtUtc] >= [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged','SessionRevokedByUser')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched')");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreference_Organization_ActiveOrganizationId",
                schema: "auth",
                table: "UserPreference",
                column: "ActiveOrganizationId",
                principalSchema: "organization",
                principalTable: "Organization",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreference_Site_ActiveOrganizationId_ActiveSiteId",
                schema: "auth",
                table: "UserPreference",
                columns: new[] { "ActiveOrganizationId", "ActiveSiteId" },
                principalSchema: "organization",
                principalTable: "Site",
                principalColumns: new[] { "OrganizationId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserPreference_Organization_ActiveOrganizationId",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPreference_Site_ActiveOrganizationId_ActiveSiteId",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropIndex(
                name: "IX_UserPreference_ActiveOrganizationId_ActiveSiteId",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserPreference_ActiveScope",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.DropColumn(
                name: "ActiveOrganizationId",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "ActiveSiteId",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "DeviceSummary",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropColumn(
                name: "IsMobileDevice",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropColumn(
                name: "LastActivityAtUtc",
                schema: "auth",
                table: "Session");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session",
                sql: "[ExpiresAtUtc] > [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated')");
        }
    }
}
