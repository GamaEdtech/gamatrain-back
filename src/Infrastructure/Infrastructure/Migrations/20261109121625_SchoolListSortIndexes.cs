using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchoolListSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasImage",
                table: "Schools",
                type: "bit",
                nullable: false,
                computedColumnSql: "CAST(CASE WHEN [DefaultImageId] IS NOT NULL THEN 1 ELSE 0 END AS bit)",
                stored: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityDate",
                table: "Schools",
                type: "datetimeoffset",
                nullable: false,
                computedColumnSql: "COALESCE([LastModifyDate], [CreationDate])",
                stored: false);

            migrationBuilder.CreateIndex(
                name: "IX_Schools_CountryId_HasImage_LastActivityDate",
                table: "Schools",
                columns: new[] { "CountryId", "HasImage", "LastActivityDate" },
                descending: new[] { false, true, true },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_HasImage_LastActivityDate",
                table: "Schools",
                columns: new[] { "HasImage", "LastActivityDate" },
                descending: new bool[0],
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_LastActivityDate",
                table: "Schools",
                column: "LastActivityDate",
                descending: new bool[0],
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schools_CountryId_HasImage_LastActivityDate",
                table: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Schools_HasImage_LastActivityDate",
                table: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Schools_LastActivityDate",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "HasImage",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "LastActivityDate",
                table: "Schools");
        }
    }
}
