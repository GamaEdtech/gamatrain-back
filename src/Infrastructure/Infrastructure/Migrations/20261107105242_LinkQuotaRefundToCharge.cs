using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkQuotaRefundToCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RefundedByUserId",
                table: "SubscriptionQuotaConsumptionLogs",
                type: "bigint",
                nullable: true);

            // Link the refunds already logged (failed downloads) to the charge each one reversed, so those charges
            // can't be refunded again by an admin: the latest earlier charge on the same subscription, feature and
            // content item. Two refunds can land on the same charge (the same item failed twice); the first keeps it
            // and the rest stay unlinked, so the unique index below can always be created.
            migrationBuilder.Sql("""
                UPDATE r SET r.RefundOfLogId = c.Id
                FROM SubscriptionQuotaConsumptionLogs r
                CROSS APPLY (
                    SELECT TOP 1 c.Id
                    FROM SubscriptionQuotaConsumptionLogs c
                    WHERE c.Amount > 0
                        AND c.Id < r.Id
                        AND c.UserSubscriptionId = r.UserSubscriptionId
                        AND c.FeatureId = r.FeatureId
                        AND (c.IdentifierId = r.IdentifierId OR (c.IdentifierId IS NULL AND r.IdentifierId IS NULL))
                    ORDER BY c.Id DESC) c
                WHERE r.Amount < 0;

                WITH d AS (
                    SELECT RefundOfLogId, ROW_NUMBER() OVER (PARTITION BY RefundOfLogId ORDER BY Id) AS rn
                    FROM SubscriptionQuotaConsumptionLogs
                    WHERE RefundOfLogId IS NOT NULL)
                UPDATE d SET RefundOfLogId = NULL WHERE rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuotaConsumptionLogs_RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs",
                column: "RefundOfLogId",
                unique: true,
                filter: "[RefundOfLogId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionQuotaConsumptionLogs_SubscriptionQuotaConsumptionLogs_RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs",
                column: "RefundOfLogId",
                principalTable: "SubscriptionQuotaConsumptionLogs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionQuotaConsumptionLogs_SubscriptionQuotaConsumptionLogs_RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionQuotaConsumptionLogs_RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs");

            migrationBuilder.DropColumn(
                name: "RefundOfLogId",
                table: "SubscriptionQuotaConsumptionLogs");

            migrationBuilder.DropColumn(
                name: "RefundedByUserId",
                table: "SubscriptionQuotaConsumptionLogs");
        }
    }
}
