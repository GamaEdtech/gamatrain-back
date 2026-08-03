using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveBillingIntervalToSubscriptionPlanPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanPrices_SubscriptionPlanId_CountryCode",
                table: "SubscriptionPlanPrices");

            migrationBuilder.AddColumn<byte>(
                name: "BillingInterval",
                table: "UserSubscriptions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "BillingInterval",
                table: "SubscriptionPlanPrices",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            // Backfill from the plan each row belongs to, while SubscriptionPlans.BillingInterval still exists
            // (dropped below). Every plan had exactly one interval before this migration, so this is exact, not a guess.
            migrationBuilder.Sql(@"
UPDATE spp
SET spp.BillingInterval = sp.BillingInterval
FROM SubscriptionPlanPrices spp
JOIN SubscriptionPlans sp ON sp.Id = spp.SubscriptionPlanId");

            migrationBuilder.Sql(@"
UPDATE us
SET us.BillingInterval = sp.BillingInterval
FROM UserSubscriptions us
JOIN SubscriptionPlans sp ON sp.Id = us.SubscriptionPlanId");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "SubscriptionPlans");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanPrices_SubscriptionPlanId_CountryCode_BillingInterval",
                table: "SubscriptionPlanPrices",
                columns: new[] { "SubscriptionPlanId", "CountryCode", "BillingInterval" },
                unique: true,
                filter: "[CountryCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanPrices_SubscriptionPlanId_CountryCode_BillingInterval",
                table: "SubscriptionPlanPrices");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "SubscriptionPlanPrices");

            migrationBuilder.AddColumn<byte>(
                name: "BillingInterval",
                table: "SubscriptionPlans",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanPrices_SubscriptionPlanId_CountryCode",
                table: "SubscriptionPlanPrices",
                columns: new[] { "SubscriptionPlanId", "CountryCode" },
                unique: true,
                filter: "[CountryCode] IS NOT NULL");
        }
    }
}
