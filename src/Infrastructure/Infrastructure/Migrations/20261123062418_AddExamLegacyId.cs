using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExamLegacyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LegacyId",
                table: "Exams",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LegacyId",
                table: "ExamQuestions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_LegacyId",
                table: "Exams",
                column: "LegacyId",
                unique: true,
                filter: "[LegacyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_LegacyId",
                table: "ExamQuestions",
                column: "LegacyId",
                unique: true,
                filter: "[LegacyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Exams_LegacyId",
                table: "Exams");

            migrationBuilder.DropIndex(
                name: "IX_ExamQuestions_LegacyId",
                table: "ExamQuestions");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "ExamQuestions");
        }
    }
}
