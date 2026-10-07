using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommissionPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommissionPayouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovalDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidByUserId = table.Column<long>(type: "bigint", nullable: true),
                    PaidDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TransferReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RejectedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RejectionDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_ApplicationUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_ApplicationUsers_PaidByUserId",
                        column: x => x.PaidByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_ApplicationUsers_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayout_UserId_Open",
                table: "CommissionPayouts",
                column: "UserId",
                unique: true,
                filter: "([Status] IN (0, 1))");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_ApprovedByUserId",
                table: "CommissionPayouts",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_PaidByUserId",
                table: "CommissionPayouts",
                column: "PaidByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_RejectedByUserId",
                table: "CommissionPayouts",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_UserId_Status",
                table: "CommissionPayouts",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommissionPayouts");
        }
    }
}
