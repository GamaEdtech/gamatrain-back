using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WeeklyReportOptedOutAt",
                table: "ApplicationUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WeeklyReportLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WeekStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyReportLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyReportLogs_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: 1L,
                column: "WeeklyReportOptedOutAt",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyReportLogs_UserId_WeekStart",
                table: "WeeklyReportLogs",
                columns: new[] { "UserId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyReportLogs_WeekStart_Kind",
                table: "WeeklyReportLogs",
                columns: new[] { "WeekStart", "Kind" });

            // The plan feature: an admin adds it to the plans that should include the report (then syncs plan features to
            // existing subscribers). No plan has it until then, so nothing is sent on deploy.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM Features WHERE Code = 'WeeklyReport')
                BEGIN
                    INSERT INTO Features (Code, Name, Description, IsActive, CreationDate)
                    VALUES ('WeeklyReport', N'Weekly reports', N'A weekly email of the user''s progress, plan usage and school', 1, SYSDATETIMEOFFSET())
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @FeatureId int = (SELECT Id FROM Features WHERE Code = 'WeeklyReport');

                IF @FeatureId IS NOT NULL
                BEGIN
                    DELETE FROM UserSubscriptionQuotaFeatures WHERE FeatureId = @FeatureId;
                    DELETE FROM SubscriptionPlanFeatures WHERE FeatureId = @FeatureId;
                    DELETE FROM Features WHERE Id = @FeatureId;
                END
                """);

            migrationBuilder.DropTable(
                name: "WeeklyReportLogs");

            migrationBuilder.DropColumn(
                name: "WeeklyReportOptedOutAt",
                table: "ApplicationUsers");
        }
    }
}
