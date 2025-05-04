using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.SqlServer.Types;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class chageHierarchyPathTypeToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "HierarchyPath",
                table: "ClassificationNode",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(SqlHierarchyId),
                oldType: "hierarchyid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<SqlHierarchyId>(
                name: "HierarchyPath",
                table: "ClassificationNode",
                type: "hierarchyid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
