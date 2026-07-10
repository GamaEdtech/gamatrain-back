using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameScoreToRankScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Score",
                table: "Schools",
                newName: "RankScore");

            migrationBuilder.RenameIndex(
                name: "IX_Schools_Score",
                table: "Schools",
                newName: "IX_Schools_RankScore");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RankScore",
                table: "Schools",
                newName: "Score");

            migrationBuilder.RenameIndex(
                name: "IX_Schools_RankScore",
                table: "Schools",
                newName: "IX_Schools_Score");
        }
    }
}
