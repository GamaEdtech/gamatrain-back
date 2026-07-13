using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentOwnerCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentOwnerCommissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    DownloaderUserId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<byte>(type: "tinyint", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    ContentType = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalContentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalFileType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalExtraId = table.Column<long>(type: "bigint", nullable: true),
                    Points = table.Column<long>(type: "bigint", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentOwnerCommissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentOwnerCommissions_ApplicationUsers_DownloaderUserId",
                        column: x => x.DownloaderUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContentOwnerCommissions_ApplicationUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentOwnerCommissions_DownloaderUserId",
                table: "ContentOwnerCommissions",
                column: "DownloaderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentOwnerCommissions_OwnerUserId",
                table: "ContentOwnerCommissions",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentOwnerCommissions");
        }
    }
}
