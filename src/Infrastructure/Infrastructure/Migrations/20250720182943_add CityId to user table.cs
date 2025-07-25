using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addCityIdtousertable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "ApplicationUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: 1,
                column: "CityId",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CityId",
                table: "ApplicationUsers");
        }
    }
}
