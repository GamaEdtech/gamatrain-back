using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExamSubmissionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExamTitle",
                table: "ExamSubmissions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Percent",
                table: "ExamSubmissions",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeTakenSeconds",
                table: "ExamSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubmissions_UserId_CreationDate",
                table: "ExamSubmissions",
                columns: new[] { "UserId", "CreationDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamSubmissions_UserId_CreationDate",
                table: "ExamSubmissions");

            migrationBuilder.DropColumn(
                name: "ExamTitle",
                table: "ExamSubmissions");

            migrationBuilder.DropColumn(
                name: "Percent",
                table: "ExamSubmissions");

            migrationBuilder.DropColumn(
                name: "TimeTakenSeconds",
                table: "ExamSubmissions");
        }
    }
}
