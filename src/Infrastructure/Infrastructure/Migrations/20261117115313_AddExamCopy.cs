using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExamCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamAnswerSheets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ExamId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptId = table.Column<long>(type: "bigint", nullable: true),
                    Answers = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAnswerSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAnswerSheets_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ExamId = table.Column<long>(type: "bigint", nullable: false),
                    ExamCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    SubmitSeconds = table.Column<int>(type: "int", nullable: true),
                    ResultScore = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: true),
                    Area = table.Column<int>(type: "int", nullable: true),
                    School = table.Column<int>(type: "int", nullable: true),
                    Transfer = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CoreOwnerId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    CodeOrder = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Parent = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    CoreBoardId = table.Column<int>(type: "int", nullable: true),
                    CoreGradeId = table.Column<int>(type: "int", nullable: true),
                    CoreCourseId = table.Column<int>(type: "int", nullable: true),
                    CoreSubjectId = table.Column<int>(type: "int", nullable: true),
                    Direction = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: true),
                    CoreTopicIds = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Level = table.Column<byte>(type: "tinyint", nullable: true),
                    Type = table.Column<byte>(type: "tinyint", nullable: true),
                    EnhancedTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuestionFile = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    AnswerA = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerB = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerC = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerD = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerFull = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerViewType = table.Column<byte>(type: "tinyint", nullable: true),
                    FileA = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    FileB = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    FileC = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    FileD = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    AnswerFullFile = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    TrueAnswer = table.Column<byte>(type: "tinyint", nullable: true),
                    Resource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UsedConfirm = table.Column<int>(type: "int", nullable: false),
                    UsedPublish = table.Column<int>(type: "int", nullable: false),
                    AnswersStat = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TeacherScore = table.Column<byte>(type: "tinyint", nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CloneId = table.Column<long>(type: "bigint", nullable: true),
                    CoreOwnerId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    CoreReferee = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    UserType = table.Column<byte>(type: "tinyint", nullable: true),
                    CoreBoardId = table.Column<int>(type: "int", nullable: true),
                    CoreGradeId = table.Column<int>(type: "int", nullable: true),
                    CoreCourseId = table.Column<int>(type: "int", nullable: true),
                    CoreSubjectId = table.Column<int>(type: "int", nullable: true),
                    CoreTopicIds = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    CoreExamTypeId = table.Column<int>(type: "int", nullable: true),
                    HoldingLevel = table.Column<byte>(type: "tinyint", nullable: true),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Area = table.Column<int>(type: "int", nullable: true),
                    School = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<byte>(type: "tinyint", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    QuestionIds = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    QuestionsCombination = table.Column<byte>(type: "tinyint", nullable: true),
                    StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    ScoreType = table.Column<byte>(type: "tinyint", nullable: true),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    EduYear = table.Column<int>(type: "int", nullable: true),
                    EduMonth = table.Column<byte>(type: "tinyint", nullable: true),
                    FileOriginal = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    FilePdf = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    PageBreakPoints = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Thumbnail = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ParticipantCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Review = table.Column<byte>(type: "tinyint", nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    DownloadCount = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAnswerSheets_AttemptId",
                table: "ExamAnswerSheets",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAnswerSheets_UserId_ExamId",
                table: "ExamAnswerSheets",
                columns: new[] { "UserId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId_Status",
                table: "ExamAttempts",
                columns: new[] { "ExamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_UserId_CreationDate",
                table: "ExamAttempts",
                columns: new[] { "UserId", "CreationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_CoreSubjectId_Status",
                table: "ExamQuestions",
                columns: new[] { "CoreSubjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_LastModifyDate",
                table: "ExamQuestions",
                column: "LastModifyDate");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_OwnerUserId",
                table: "ExamQuestions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_Code",
                table: "Exams",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CoreBoardId_CoreGradeId_Status",
                table: "Exams",
                columns: new[] { "CoreBoardId", "CoreGradeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CoreSubjectId_Status",
                table: "Exams",
                columns: new[] { "CoreSubjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_LastModifyDate",
                table: "Exams",
                column: "LastModifyDate");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_OwnerUserId",
                table: "Exams",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamAnswerSheets");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ExamQuestions");

            migrationBuilder.DropTable(
                name: "Exams");
        }
    }
}
