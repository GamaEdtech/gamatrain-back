using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanSwitchFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PendingSwitchPricePaid",
                table: "UserSubscriptions",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions",
                column: "PendingSwitchSubscriptionPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPlans_PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions",
                column: "PendingSwitchSubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPlans_PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "PendingSwitchPricePaid",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "PendingSwitchSubscriptionPlanId",
                table: "UserSubscriptions");
        }
    }
}
