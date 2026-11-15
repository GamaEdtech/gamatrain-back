using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPastPapers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PastPapers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreOwnerId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    CoreTeacherId = table.Column<long>(type: "bigint", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CoreBoardId = table.Column<int>(type: "int", nullable: false),
                    CoreGradeId = table.Column<int>(type: "int", nullable: false),
                    CoreSubjectId = table.Column<int>(type: "int", nullable: false),
                    CoreCourseId = table.Column<int>(type: "int", nullable: true),
                    CoreTopicIds = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CorePaperTypeId = table.Column<int>(type: "int", nullable: true),
                    Variant = table.Column<short>(type: "smallint", nullable: true),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Level = table.Column<byte>(type: "tinyint", nullable: true),
                    AnswerType = table.Column<byte>(type: "tinyint", nullable: true),
                    HoldingLevel = table.Column<short>(type: "smallint", nullable: true),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CoreAreaId = table.Column<int>(type: "int", nullable: true),
                    CoreSchoolId = table.Column<long>(type: "bigint", nullable: true),
                    EduYear = table.Column<short>(type: "smallint", nullable: true),
                    EduMonth = table.Column<byte>(type: "tinyint", nullable: true),
                    EduSession = table.Column<byte>(type: "tinyint", nullable: true),
                    CoreExamIds = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    IsPaper = table.Column<bool>(type: "bit", nullable: false),
                    FreeAgreement = table.Column<bool>(type: "bit", nullable: false),
                    PriceMultiplier = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    RefereeScore = table.Column<byte>(type: "tinyint", nullable: false),
                    CoreReferee = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false),
                    TeacherScore = table.Column<byte>(type: "tinyint", nullable: false),
                    Thumbnail = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    DownloadCount = table.Column<int>(type: "int", nullable: false),
                    PaidCount = table.Column<int>(type: "int", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastPapers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PastPapers_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PastPaperFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PastPaperId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Path = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Extension = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    PageCount = table.Column<short>(type: "smallint", nullable: true),
                    Length = table.Column<int>(type: "int", nullable: true),
                    CoreTypeId = table.Column<int>(type: "int", nullable: true),
                    CoreExtraFileId = table.Column<long>(type: "bigint", nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastPaperFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PastPaperFiles_PastPapers_PastPaperId",
                        column: x => x.PastPaperId,
                        principalTable: "PastPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PastPaperFiles_CoreExtraFileId",
                table: "PastPaperFiles",
                column: "CoreExtraFileId",
                unique: true,
                filter: "[CoreExtraFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PastPaperFiles_PastPaperId_Kind",
                table: "PastPaperFiles",
                columns: new[] { "PastPaperId", "Kind" },
                unique: true,
                filter: "[Kind] <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_CoreBoardId_CoreGradeId_Status_IsDeleted",
                table: "PastPapers",
                columns: new[] { "CoreBoardId", "CoreGradeId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_CoreOwnerId",
                table: "PastPapers",
                column: "CoreOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_CoreSubjectId_Status_IsDeleted",
                table: "PastPapers",
                columns: new[] { "CoreSubjectId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_CreationDate",
                table: "PastPapers",
                column: "CreationDate");

            migrationBuilder.CreateIndex(
                name: "IX_PastPapers_OwnerUserId_Status",
                table: "PastPapers",
                columns: new[] { "OwnerUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PastPaperFiles");

            migrationBuilder.DropTable(
                name: "PastPapers");
        }
    }
}
