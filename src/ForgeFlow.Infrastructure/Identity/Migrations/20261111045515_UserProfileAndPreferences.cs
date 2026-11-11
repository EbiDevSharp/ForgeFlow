using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserProfileAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.AddColumn<string>(
                name: "AvatarContentType",
                schema: "auth",
                table: "User",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "AvatarData",
                schema: "auth",
                table: "User",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "auth",
                table: "User",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserPreference",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Language = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    Theme = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CalendarSystem = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreference", x => x.UserId);
                    table.CheckConstraint("CK_UserPreference_Calendar", "[CalendarSystem] COLLATE Latin1_General_100_BIN2 IN ('persian','gregorian')");
                    table.CheckConstraint("CK_UserPreference_Language", "DATALENGTH([Language]) = 5 AND [Language] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US')");
                    table.CheckConstraint("CK_UserPreference_Metadata", "[UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_UserPreference_Theme", "[Theme] COLLATE Latin1_General_100_BIN2 IN ('light','dark','system')");
                    table.CheckConstraint("CK_UserPreference_TimeZone", "LEN([TimeZoneId]) BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "FK_UserPreference_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_Avatar",
                schema: "auth",
                table: "User",
                sql: "([AvatarContentType] IS NULL AND [AvatarData] IS NULL) OR ([AvatarContentType] IN ('image/png','image/jpeg','image/webp') AND [AvatarData] IS NOT NULL AND DATALENGTH([AvatarData]) BETWEEN 1 AND 262144)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated')");

            migrationBuilder.Sql(
                """
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserPreference] TO [ForgeFlowAuthRuntime];
                    DENY DELETE ON OBJECT::[auth].[UserPreference] TO [ForgeFlowAuthRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [auth].[UserPreference])
                    THROW 50004, 'Auth.NonEmptyRollbackDenied', 1;
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserPreference] FROM [ForgeFlowAuthRuntime];
                END;
                """);

            migrationBuilder.DropTable(
                name: "UserPreference",
                schema: "auth");

            migrationBuilder.DropCheckConstraint(
                name: "CK_User_Avatar",
                schema: "auth",
                table: "User");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent");

            migrationBuilder.DropColumn(
                name: "AvatarContentType",
                schema: "auth",
                table: "User");

            migrationBuilder.DropColumn(
                name: "AvatarData",
                schema: "auth",
                table: "User");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "auth",
                table: "User");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvent_Code",
                schema: "auth",
                table: "SecurityEvent",
                sql: "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged')");
        }
    }
}
