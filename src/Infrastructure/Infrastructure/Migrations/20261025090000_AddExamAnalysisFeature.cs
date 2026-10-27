using GamaEdtech.Infrastructure.EntityFramework.Context;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDBContext))]
    [Migration("20261025090000_AddExamAnalysisFeature")]
    public partial class AddExamAnalysisFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM Features WHERE Code = 'ExamAnalysis')
            BEGIN
                INSERT INTO Features (Code, Name, Description, IsActive, CreationDate)
                VALUES ('ExamAnalysis', N'Exam Analyses', N'AI-generated analysis of a completed exam', 1, SYSDATETIMEOFFSET())
            END
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            DECLARE @FeatureId int = (SELECT Id FROM Features WHERE Code = 'ExamAnalysis');

            IF @FeatureId IS NOT NULL
            BEGIN
                DELETE FROM UserSubscriptionQuotaFeatures WHERE FeatureId = @FeatureId;
                DELETE FROM SubscriptionPlanFeatures WHERE FeatureId = @FeatureId;
                DELETE FROM Features WHERE Id = @FeatureId;
            END
            """);
    }
}
