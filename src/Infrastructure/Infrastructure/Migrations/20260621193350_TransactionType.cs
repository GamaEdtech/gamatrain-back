using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransactionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "TransactionType",
                table: "Transactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionType",
                table: "Transactions",
                column: "TransactionType");

            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.SuccessfulContribution.Value} WHERE Description LIKE '%Successful Contribution%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.EasterEgg.Value} WHERE Description LIKE '%the Easter Egg game%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.CorrectTestTimeSubmission.Value} WHERE Description LIKE '%TestTime Correct Submission%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.CorrectExamSubmission.Value} WHERE IsDebit = 0 AND Description LIKE '%Exam Submission%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.IncorrectExamSubmission.Value} WHERE IsDebit = 1 AND Description LIKE '%Exam Submission%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.Payment.Value} WHERE Description LIKE '%Payment%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.AdminIncreaseBalance.Value} WHERE IsDebit = 0 AND Description LIKE '%Admin CreateTransaction%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.AdminDecreaseBalance.Value} WHERE IsDebit = 1 AND Description LIKE '%Admin CreateTransaction%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.DeleteContribution.Value} WHERE Description LIKE '%Delete Contribution%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.DownloadPastPaper.Value} WHERE Description LIKE '%Spend Game Points - {GamaEdtech.Domain.Enumeration.ContentType.PastPaper.Name}%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.DownloadTest.Value} WHERE Description LIKE '%Spend Game Points - {GamaEdtech.Domain.Enumeration.ContentType.Test.Name}%'");
            migrationBuilder.Sql($"UPDATE Transactions SET TransactionType = {GamaEdtech.Domain.Enumeration.TransactionType.IncorrectTestTimeSubmission.Value} WHERE Description LIKE '%TestTime Incorrect Submission%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransactionType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransactionType",
                table: "Transactions");
        }
    }
}
