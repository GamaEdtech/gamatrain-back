using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tutorials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreOwnerId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    DisplayOwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    CoreReferee = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    CoreBoardId = table.Column<int>(type: "int", nullable: true),
                    CoreGradeId = table.Column<int>(type: "int", nullable: true),
                    CoreCourseId = table.Column<int>(type: "int", nullable: true),
                    CoreSubjectId = table.Column<int>(type: "int", nullable: true),
                    CoreTopicIds = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    TopicOrder = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentSize = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tutorials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tutorials_ApplicationUsers_DisplayOwnerUserId",
                        column: x => x.DisplayOwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tutorials_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TutorialImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TutorialId = table.Column<long>(type: "bigint", nullable: false),
                    CoreFileId = table.Column<long>(type: "bigint", nullable: false),
                    Path = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorialImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorialImages_Tutorials_TutorialId",
                        column: x => x.TutorialId,
                        principalTable: "Tutorials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TutorialImages_CoreFileId",
                table: "TutorialImages",
                column: "CoreFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TutorialImages_TutorialId",
                table: "TutorialImages",
                column: "TutorialId");

            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_CoreBoardId_CoreGradeId_Status",
                table: "Tutorials",
                columns: new[] { "CoreBoardId", "CoreGradeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_CoreSubjectId_Status",
                table: "Tutorials",
                columns: new[] { "CoreSubjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_DisplayOwnerUserId",
                table: "Tutorials",
                column: "DisplayOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_LastModifyDate",
                table: "Tutorials",
                column: "LastModifyDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tutorials_OwnerUserId",
                table: "Tutorials",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TutorialImages");

            migrationBuilder.DropTable(
                name: "Tutorials");
        }
    }
}
