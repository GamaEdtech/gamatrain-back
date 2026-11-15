using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPastPaperFileSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckDate",
                table: "PastPaperFiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Size",
                table: "PastPaperFiles",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckDate",
                table: "PastPaperFiles");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "PastPaperFiles");
        }
    }
}
