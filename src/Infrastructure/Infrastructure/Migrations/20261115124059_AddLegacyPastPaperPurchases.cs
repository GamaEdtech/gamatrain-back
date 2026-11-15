using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyPastPaperPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyPastPaperPurchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    CoreUsageId = table.Column<long>(type: "bigint", nullable: false),
                    CoreType = table.Column<byte>(type: "tinyint", nullable: false),
                    CoreStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    PurchaseDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PastPaperId = table.Column<long>(type: "bigint", nullable: true),
                    Variant = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContentPurchaseId = table.Column<long>(type: "bigint", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPastPaperPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyPastPaperPurchases_ContentPurchases_ContentPurchaseId",
                        column: x => x.ContentPurchaseId,
                        principalTable: "ContentPurchases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPastPaperPurchases_ContentPurchaseId",
                table: "LegacyPastPaperPurchases",
                column: "ContentPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPastPaperPurchases_CoreUserId",
                table: "LegacyPastPaperPurchases",
                column: "CoreUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyPastPaperPurchases");
        }
    }
}
