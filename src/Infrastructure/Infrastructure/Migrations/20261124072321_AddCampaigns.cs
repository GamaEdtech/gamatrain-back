using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CampaignId",
                table: "Payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Theme = table.Column<byte>(type: "tinyint", nullable: false),
                    DiscountPercent = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RepeatsYearly = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreationUserId = table.Column<long>(type: "bigint", nullable: false),
                    LastModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifyUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CampaignId",
                table: "Payments",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_IsEnabled_EndsAt",
                table: "Campaigns",
                columns: new[] { "IsEnabled", "EndsAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Campaigns_CampaignId",
                table: "Payments",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Campaigns_CampaignId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CampaignId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Payments");
        }
    }
}
