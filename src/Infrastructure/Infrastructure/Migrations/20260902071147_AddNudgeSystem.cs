using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNudgeSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NudgeTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NudgeType = table.Column<byte>(type: "tinyint", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CtaLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CtaUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NudgeTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNudgeLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    NudgeType = table.Column<byte>(type: "tinyint", nullable: false),
                    LastSentDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SendCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNudgeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNudgeLogs_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NudgeTemplates_NudgeType",
                table: "NudgeTemplates",
                column: "NudgeType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNudgeLogs_UserId_NudgeType",
                table: "UserNudgeLogs",
                columns: new[] { "UserId", "NudgeType" },
                unique: true);

            // Seed default (admin-editable) templates so the feature works immediately after deploy, without
            // requiring manual setup first - see docs/business/notifications.md, "Nudge system".
            migrationBuilder.InsertData(
                table: "NudgeTemplates",
                columns: new[] { "NudgeType", "Subject", "Body", "CtaLabel", "CtaUrl", "IsActive", "CreationDate" },
                values: new object[,]
                {
                    { (byte)0, "Tell us: are you a teacher or a student?", "Hi [RECEIVER_NAME],<br><br>Choosing your role unlocks the right dashboard and content for you.<br><br><a href=\"[CTA_URL]\">Choose your role</a>", "Choose your role", "https://gamatrain.com/user/type", true, DateTimeOffset.UtcNow },
                    { (byte)1, "Add a profile photo", "Hi [RECEIVER_NAME],<br><br>Profiles with a photo get noticed more. Add yours in a minute.<br><br><a href=\"[CTA_URL]\">Add photo</a>", "Add photo", "https://gamatrain.com/user/profile", true, DateTimeOffset.UtcNow },
                    { (byte)2, "Add your name to your profile", "Hi [RECEIVER_NAME],<br><br>Your profile is missing your name - add it so others know who you are.<br><br><a href=\"[CTA_URL]\">Add your name</a>", "Add your name", "https://gamatrain.com/user/profile", true, DateTimeOffset.UtcNow },
                    { (byte)3, "Tell the community about yourself", "Hi [RECEIVER_NAME],<br><br>A short bio helps others get to know you.<br><br><a href=\"[CTA_URL]\">Add your bio</a>", "Add your bio", "https://gamatrain.com/user/profile", true, DateTimeOffset.UtcNow },
                    { (byte)4, "Add your skills", "Hi [RECEIVER_NAME],<br><br>Listing your skills helps you stand out.<br><br><a href=\"[CTA_URL]\">Add skills</a>", "Add skills", "https://gamatrain.com/user/profile", true, DateTimeOffset.UtcNow },
                    { (byte)5, "Add your experience", "Hi [RECEIVER_NAME],<br><br>Adding your experience builds trust with the community.<br><br><a href=\"[CTA_URL]\">Add experience</a>", "Add experience", "https://gamatrain.com/user/profile", true, DateTimeOffset.UtcNow },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NudgeTemplates");

            migrationBuilder.DropTable(
                name: "UserNudgeLogs");
        }
    }
}
