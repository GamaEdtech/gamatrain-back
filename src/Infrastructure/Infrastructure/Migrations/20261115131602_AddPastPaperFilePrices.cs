using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPastPaperFilePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PastPaperFilePrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreType = table.Column<byte>(type: "tinyint", nullable: false),
                    CorePaperTypeId = table.Column<int>(type: "int", nullable: false),
                    Extensions = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    BasePrice = table.Column<int>(type: "int", nullable: false),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastPaperFilePrices", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PastPaperFilePrices");
        }
    }
}
