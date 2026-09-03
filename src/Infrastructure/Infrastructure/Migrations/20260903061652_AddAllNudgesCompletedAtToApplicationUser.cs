using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAllNudgesCompletedAtToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AllNudgesCompletedAt",
                table: "ApplicationUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: 1L,
                column: "AllNudgesCompletedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllNudgesCompletedAt",
                table: "ApplicationUsers");
        }
    }
}
