using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPastPaperPendingEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingDescription",
                table: "PastPapers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingEditDate",
                table: "PastPapers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingEditNote",
                table: "PastPapers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PastPaperPendingFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PastPaperId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    CoreExtraFileId = table.Column<long>(type: "bigint", nullable: true),
                    IsRemoval = table.Column<bool>(type: "bit", nullable: false),
                    Path = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Extension = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    PageCount = table.Column<short>(type: "smallint", nullable: true),
                    CoreTypeId = table.Column<int>(type: "int", nullable: true),
                    Thumbnail = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastPaperPendingFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PastPaperPendingFiles_PastPapers_PastPaperId",
                        column: x => x.PastPaperId,
                        principalTable: "PastPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PastPaperPendingFiles_PastPaperId",
                table: "PastPaperPendingFiles",
                column: "PastPaperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PastPaperPendingFiles");

            migrationBuilder.DropColumn(
                name: "PendingDescription",
                table: "PastPapers");

            migrationBuilder.DropColumn(
                name: "PendingEditDate",
                table: "PastPapers");

            migrationBuilder.DropColumn(
                name: "PendingEditNote",
                table: "PastPapers");
        }
    }
}
