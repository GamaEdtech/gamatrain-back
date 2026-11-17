using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TopicMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    GamaTopicId = table.Column<int>(type: "int", nullable: false),
                    GamaSubjectId = table.Column<int>(type: "int", nullable: false),
                    ExternalTopicId = table.Column<int>(type: "int", nullable: false),
                    ExternalSubjectId = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<byte>(type: "tinyint", nullable: false),
                    Reasoning = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AiModel = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CoreReviewedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TopicMatches_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TopicMatches_ExternalTopicId",
                table: "TopicMatches",
                column: "ExternalTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_TopicMatches_GamaSubjectId_Status",
                table: "TopicMatches",
                columns: new[] { "GamaSubjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TopicMatches_GamaTopicId_ExternalTopicId",
                table: "TopicMatches",
                columns: new[] { "GamaTopicId", "ExternalTopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TopicMatches_ReviewedByUserId",
                table: "TopicMatches",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TopicMatches_Status",
                table: "TopicMatches",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicMatches");
        }
    }
}
