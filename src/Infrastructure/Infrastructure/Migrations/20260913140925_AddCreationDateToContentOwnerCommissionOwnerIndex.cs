using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreationDateToContentOwnerCommissionOwnerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentOwnerCommissions_OwnerUserId",
                table: "ContentOwnerCommissions");

            migrationBuilder.CreateIndex(
                name: "IX_ContentOwnerCommissions_OwnerUserId_CreationDate",
                table: "ContentOwnerCommissions",
                columns: new[] { "OwnerUserId", "CreationDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentOwnerCommissions_OwnerUserId_CreationDate",
                table: "ContentOwnerCommissions");

            migrationBuilder.CreateIndex(
                name: "IX_ContentOwnerCommissions_OwnerUserId",
                table: "ContentOwnerCommissions",
                column: "OwnerUserId");
        }
    }
}
