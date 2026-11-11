using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserNotificationsAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNotification",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    TemplateCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ParamsJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotification", x => x.Id);
                    table.CheckConstraint("CK_UserNotification_Category", "[Category] IN (1, 2)");
                    table.CheckConstraint("CK_UserNotification_Params", "ISJSON([ParamsJson]) = 1");
                    table.CheckConstraint("CK_UserNotification_ReadAt", "[ReadAtUtc] IS NULL OR [ReadAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_UserNotification_Severity", "[Severity] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_UserNotification_Template", "DATALENGTH([TemplateCode])=LEN([TemplateCode]) AND [TemplateCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempts','RoleOrAccessChanged','MyProductionOrderStatusChanged','PasswordChangedAlert')");
                    table.ForeignKey(
                        name: "FK_UserNotification_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreference",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    EventCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InAppEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreference", x => new { x.UserId, x.EventCode });
                    table.CheckConstraint("CK_UserNotificationPreference_EventCode", "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempt','RoleOrAccessChanged','MyProductionOrders')");
                    table.CheckConstraint("CK_UserNotificationPreference_Metadata", "[UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_UserNotificationPreference_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotification_UserId_ReadAtUtc_CreatedAtUtc_Id",
                schema: "auth",
                table: "UserNotification",
                columns: new[] { "UserId", "ReadAtUtc", "CreatedAtUtc", "Id" });

            migrationBuilder.Sql(
                """
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserNotificationPreference] TO [ForgeFlowAuthRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserNotification] TO [ForgeFlowAuthRuntime];
                    DENY DELETE ON OBJECT::[auth].[UserNotificationPreference] TO [ForgeFlowAuthRuntime];
                    DENY DELETE ON OBJECT::[auth].[UserNotification] TO [ForgeFlowAuthRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [auth].[UserNotification])
                    OR EXISTS (SELECT 1 FROM [auth].[UserNotificationPreference])
                    THROW 50005, 'Auth.NonEmptyRollbackDenied', 1;
                IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
                BEGIN
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserNotificationPreference] FROM [ForgeFlowAuthRuntime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserNotification] FROM [ForgeFlowAuthRuntime];
                END;
                """);

            migrationBuilder.DropTable(
                name: "UserNotification",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserNotificationPreference",
                schema: "auth");
        }
    }
}
