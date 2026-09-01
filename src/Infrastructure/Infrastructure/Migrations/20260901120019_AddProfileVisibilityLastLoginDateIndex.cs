using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileVisibilityLastLoginDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_ProfileVisibility_LastLoginDate",
                table: "ApplicationUsers",
                columns: new[] { "ProfileVisibility", "LastLoginDate" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_ProfileVisibility_LastLoginDate",
                table: "ApplicationUsers");
        }
    }
}
