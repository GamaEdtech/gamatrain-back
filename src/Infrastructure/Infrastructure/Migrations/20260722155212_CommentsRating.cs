using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommentsRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schools_CountryId_IsDeleted",
                table: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Schools_Rating",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Schools");

            migrationBuilder.AddColumn<int>(
                name: "CommentsRatingSum",
                table: "Schools",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommentsRatingCount",
                table: "Schools",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommentsRatingCount",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "CommentsRatingSum",
                table: "Schools");

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Schools",
                type: "float",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Rating",
                table: "Schools",
                column: "Rating",
                descending: new bool[0]);
        }
    }
}
