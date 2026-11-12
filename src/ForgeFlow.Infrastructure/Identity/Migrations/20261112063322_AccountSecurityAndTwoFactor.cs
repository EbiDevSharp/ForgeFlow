using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AccountSecurityAndTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserNotification_Template",
                schema: "auth",
                table: "UserNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.AddColumn<int>(
                name: "IdleTimeoutMinutes",
                schema: "auth",
                table: "UserPreference",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorOneTimeNotifiedAtUtc",
                schema: "auth",
                table: "UserPreference",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorReminderSnoozedUntilUtc",
                schema: "auth",
                table: "UserPreference",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                schema: "auth",
                table: "User",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserPreference_IdleTimeout",
                schema: "auth",
                table: "UserPreference",
                sql: "[IdleTimeoutMinutes] IS NULL OR [IdleTimeoutMinutes] IN (15,30,60)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserNotification_Template",
                schema: "auth",
                table: "UserNotification",
                sql: "DATALENGTH([TemplateCode])=LEN([TemplateCode]) AND [TemplateCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempts','RoleOrAccessChanged','MyProductionOrderStatusChanged','PasswordChangedAlert','TwoFactorReminder')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session",
                sql: "[ExpiresAtUtc] > [IssuedAtUtc] AND [LastActivityAtUtc] >= [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged','SessionRevokedByUser','IdleTimeout')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched','TwoFactorEnabled','TwoFactorDisabled','RecoveryCodesGenerated','RecoveryCodeRedeemed','IdleTimeoutUpdated','DeviceTrusted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserPreference_IdleTimeout",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserNotification_Template",
                schema: "auth",
                table: "UserNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Session_Lifecycle",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.DropColumn(
                name: "IdleTimeoutMinutes",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "TwoFactorOneTimeNotifiedAtUtc",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "TwoFactorReminderSnoozedUntilUtc",
                schema: "auth",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                schema: "auth",
                table: "User");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserNotification_Template",
                schema: "auth",
                table: "UserNotification",
                sql: "DATALENGTH([TemplateCode])=LEN([TemplateCode]) AND [TemplateCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempts','RoleOrAccessChanged','MyProductionOrderStatusChanged','PasswordChangedAlert')");

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
        }
    }
}
