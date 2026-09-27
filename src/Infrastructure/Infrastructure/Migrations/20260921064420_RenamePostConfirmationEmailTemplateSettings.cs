using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamePostConfirmationEmailTemplateSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ApplicationSettings rows are keyed by the DTO property name, so a renamed property would otherwise
            // orphan the stored template and silently fall back to the default text.
            migrationBuilder.Sql("""
UPDATE ApplicationSettings SET Id = N'PostConfirmationEmailTemplate'
WHERE Id = N'PostContributionConfirmationEmailTemplate' AND NOT EXISTS (SELECT 1 FROM ApplicationSettings WHERE Id = N'PostConfirmationEmailTemplate');
""");
            // ApplicationSettings rows are keyed by the DTO property name, so a renamed property would otherwise
            // orphan the stored template and silently fall back to the default text.
            migrationBuilder.Sql("""
UPDATE ApplicationSettings SET Id = N'PostCommentConfirmationEmailTemplate'
WHERE Id = N'PostCommentContributionConfirmationEmailTemplate' AND NOT EXISTS (SELECT 1 FROM ApplicationSettings WHERE Id = N'PostCommentConfirmationEmailTemplate');
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE ApplicationSettings SET Id = N'PostContributionConfirmationEmailTemplate'
WHERE Id = N'PostConfirmationEmailTemplate' AND NOT EXISTS (SELECT 1 FROM ApplicationSettings WHERE Id = N'PostContributionConfirmationEmailTemplate');
""");
            migrationBuilder.Sql("""
UPDATE ApplicationSettings SET Id = N'PostCommentContributionConfirmationEmailTemplate'
WHERE Id = N'PostCommentConfirmationEmailTemplate' AND NOT EXISTS (SELECT 1 FROM ApplicationSettings WHERE Id = N'PostCommentContributionConfirmationEmailTemplate');
""");
        }
    }
}
