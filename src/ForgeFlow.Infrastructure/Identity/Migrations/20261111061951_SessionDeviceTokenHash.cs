using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SessionDeviceTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceTokenHash",
                schema: "auth",
                table: "Session",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Session_UserId_DeviceTokenHash",
                schema: "auth",
                table: "Session",
                columns: new[] { "UserId", "DeviceTokenHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [auth].[Session] WHERE [DeviceTokenHash] IS NOT NULL)
                    THROW 50005, 'Auth.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Session_UserId_DeviceTokenHash",
                schema: "auth",
                table: "Session");

            migrationBuilder.DropColumn(
                name: "DeviceTokenHash",
                schema: "auth",
                table: "Session");
        }
    }
}
