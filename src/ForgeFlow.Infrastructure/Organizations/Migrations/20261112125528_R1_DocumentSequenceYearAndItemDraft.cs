using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class R1_DocumentSequenceYearAndItemDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Item_Capability",
                schema: "organization",
                table: "Item");

            migrationBuilder.AddColumn<int>(
                name: "LastAllocatedYear",
                schema: "organization",
                table: "DocumentSequence",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAllocatedYear",
                schema: "organization",
                table: "DocumentSequence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Item_Capability",
                schema: "organization",
                table: "Item",
                sql: "[CanBePurchased] = 1 OR [CanBeManufactured] = 1 OR [CanBeSold] = 1 OR [CanBeConsumed] = 1");
        }
    }
}
