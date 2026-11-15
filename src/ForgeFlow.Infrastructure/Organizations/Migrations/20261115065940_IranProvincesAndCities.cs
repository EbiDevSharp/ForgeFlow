using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class IranProvincesAndCities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IranProvince",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IranProvince", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IranCity",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ProvinceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IranCity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IranCity_IranProvince",
                        column: x => x.ProvinceId,
                        principalSchema: "organization",
                        principalTable: "IranProvince",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IranCity_ProvinceId",
                schema: "organization",
                table: "IranCity",
                column: "ProvinceId");

            SeedProvincesAndCities(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IranCity",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "IranProvince",
                schema: "organization");
        }
    }
}
