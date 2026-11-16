using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyTaxonomyItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyTaxonomyItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<short>(type: "smallint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ListOrder = table.Column<int>(type: "int", nullable: false),
                    Parent = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Parent2 = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSeason = table.Column<bool>(type: "bit", nullable: true),
                    IsPaper = table.Column<bool>(type: "bit", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyTaxonomyItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegacyTaxonomyItemParents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ParentKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyTaxonomyItemParents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyTaxonomyItemParents_LegacyTaxonomyItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "LegacyTaxonomyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyTaxonomyItemParents_ItemId",
                table: "LegacyTaxonomyItemParents",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyTaxonomyItemParents_ParentKey_ItemId",
                table: "LegacyTaxonomyItemParents",
                columns: new[] { "ParentKey", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyTaxonomyItems_Kind_IsDeleted_ListOrder",
                table: "LegacyTaxonomyItems",
                columns: new[] { "Kind", "IsDeleted", "ListOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyTaxonomyItemParents");

            migrationBuilder.DropTable(
                name: "LegacyTaxonomyItems");
        }
    }
}
