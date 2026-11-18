using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeExamAttemptStatusConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing in the database changes: ExamAttempts.Status becomes a concurrency token, which only adds
            // "AND Status = <old value>" to EF's updates of an attempt.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing in the database to undo; see Up.
        }
    }
}
