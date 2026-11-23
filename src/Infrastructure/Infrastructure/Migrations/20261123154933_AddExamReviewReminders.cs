using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExamReviewReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamReviewReminders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ExamId = table.Column<long>(type: "bigint", nullable: false),
                    SentDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamReviewReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamReviewReminders_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamReviewReminders_UserId_ExamId",
                table: "ExamReviewReminders",
                columns: new[] { "UserId", "ExamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamReviewReminders_UserId_SentDate",
                table: "ExamReviewReminders",
                columns: new[] { "UserId", "SentDate" });

            // The exam review reminder (NudgeType.ExamReview = 7), admin-editable like the other nudge templates and on from
            // the start: one email per finished exam, only to someone who hasn't reviewed it. [EXAM_ID] and [EXAM_SLUG] in
            // CtaUrl are the exam's; #reviews opens its page at the reviews.
            migrationBuilder.InsertData(
                table: "NudgeTemplates",
                columns: new[] { "NudgeType", "Subject", "Body", "CtaLabel", "CtaUrl", "IsActive", "CreationDate" },
                values: new object[]
                {
                    (byte)7,
                    "How was [EXAM_TITLE]?",
                    "Hi [RECEIVER_NAME],<br><br>Thanks for taking <b>[EXAM_TITLE]</b>. How did you find it? A quick rating helps other students choose their next practice exam, and helps the teacher improve it.<br><br><a href=\"[CTA_URL]\">Rate this exam</a><br><br>It takes less than a minute.",
                    "Rate this exam",
                    "https://gamatrain.com/exam/[EXAM_ID]/[EXAM_SLUG]#reviews",
                    true,
                    DateTimeOffset.UtcNow,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [NudgeTemplates] WHERE [NudgeType] = 7");

            migrationBuilder.DropTable(
                name: "ExamReviewReminders");
        }
    }
}
