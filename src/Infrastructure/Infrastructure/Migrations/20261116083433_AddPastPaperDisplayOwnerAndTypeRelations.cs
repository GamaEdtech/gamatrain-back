using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPastPaperDisplayOwnerAndTypeRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DisplayOwnerUserId",
                table: "PastPapers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegacyTaxonomyRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    FromId = table.Column<int>(type: "int", nullable: false),
                    ToId = table.Column<int>(type: "int", nullable: false),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyTaxonomyRelations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_DisplayOwnerUserId",
                table: "PastPapers",
                column: "DisplayOwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PastPapers_ApplicationUsers_DisplayOwnerUserId",
                table: "PastPapers",
                column: "DisplayOwnerUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PastPapers_ApplicationUsers_DisplayOwnerUserId",
                table: "PastPapers");

            migrationBuilder.DropTable(
                name: "LegacyTaxonomyRelations");

            migrationBuilder.DropIndex(
                name: "IX_PastPapers_DisplayOwnerUserId",
                table: "PastPapers");

            migrationBuilder.DropColumn(
                name: "DisplayOwnerUserId",
                table: "PastPapers");
        }
    }
}
