using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserPasskeysWebAuthn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.CreateTable(
                name: "UserPasskey",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: false),
                    PublicKeyCose = table.Column<byte[]>(type: "varbinary(1024)", maxLength: 1024, nullable: false),
                    Algorithm = table.Column<int>(type: "int", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasskey", x => x.Id);
                    table.CheckConstraint("CK_UserPasskey_Algo", "[Algorithm] IN (-7, -257)");
                    table.CheckConstraint("CK_UserPasskey_Name", "LEN([DisplayName]) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_UserPasskey_SignCount", "[SignCount] >= 0");
                    table.ForeignKey(
                        name: "FK_UserPasskey_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched','TwoFactorEnabled','TwoFactorDisabled','RecoveryCodesGenerated','RecoveryCodeRedeemed','IdleTimeoutUpdated','DeviceTrusted','PasskeyRegistered','PasskeyRemoved','PasskeySignIn')");

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskey_UserId_RevokedAtUtc",
                schema: "auth",
                table: "UserPasskey",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_UserPasskey_CredentialId",
                schema: "auth",
                table: "UserPasskey",
                column: "CredentialId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPasskey",
                schema: "auth");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched','TwoFactorEnabled','TwoFactorDisabled','RecoveryCodesGenerated','RecoveryCodeRedeemed','IdleTimeoutUpdated','DeviceTrusted')");
        }
    }
}
