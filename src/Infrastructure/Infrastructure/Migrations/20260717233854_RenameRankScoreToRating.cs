using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRankScoreToRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RankScore",
                table: "Schools",
                newName: "Rating");

            migrationBuilder.RenameIndex(
                name: "IX_Schools_RankScore",
                table: "Schools",
                newName: "IX_Schools_Rating");

            migrationBuilder.Sql("UPDATE Schools SET Rating=NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Rating",
                table: "Schools",
                newName: "RankScore");

            migrationBuilder.RenameIndex(
                name: "IX_Schools_Rating",
                table: "Schools",
                newName: "IX_Schools_RankScore");
        }
    }
}
