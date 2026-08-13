using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionQuotaConsumptionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionQuotaConsumptionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    UserSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    IdentifierId = table.Column<long>(type: "bigint", nullable: true),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionQuotaConsumptionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionQuotaConsumptionLogs_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionQuotaConsumptionLogs_UserSubscriptions_UserSubscriptionId",
                        column: x => x.UserSubscriptionId,
                        principalTable: "UserSubscriptions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuotaConsumptionLogs_FeatureId_CreationDate",
                table: "SubscriptionQuotaConsumptionLogs",
                columns: new[] { "FeatureId", "CreationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuotaConsumptionLogs_UserId_CreationDate",
                table: "SubscriptionQuotaConsumptionLogs",
                columns: new[] { "UserId", "CreationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuotaConsumptionLogs_UserSubscriptionId",
                table: "SubscriptionQuotaConsumptionLogs",
                column: "UserSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionQuotaConsumptionLogs");
        }
    }
}
