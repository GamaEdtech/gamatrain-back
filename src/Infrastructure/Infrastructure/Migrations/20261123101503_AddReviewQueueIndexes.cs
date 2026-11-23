using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewQueueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_Review_CreationDate",
                table: "Tutorials",
                column: "CreationDate",
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_Review_CreationDate",
                table: "PastPapers",
                column: "CreationDate",
                filter: "[Status] IN (0, 6) AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_Review_CreationDate",
                table: "Exams",
                column: "CreationDate",
                filter: "[Status] IN (0, 7) AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_Review_CreationDate",
                table: "ExamQuestions",
                column: "CreationDate",
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tutorials_Review_CreationDate",
                table: "Tutorials");

            migrationBuilder.DropIndex(
                name: "IX_PastPapers_Review_CreationDate",
                table: "PastPapers");

            migrationBuilder.DropIndex(
                name: "IX_Exams_Review_CreationDate",
                table: "Exams");

            migrationBuilder.DropIndex(
                name: "IX_ExamQuestions_Review_CreationDate",
                table: "ExamQuestions");
        }
    }
}
