using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingIntervalToSubscriptionPlanFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanFeatures_SubscriptionPlanId_FeatureId",
                table: "SubscriptionPlanFeatures");

            migrationBuilder.AddColumn<byte>(
                name: "BillingInterval",
                table: "SubscriptionPlanFeatures",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)2 /* Monthly */);

            // Backfill: today every SubscriptionPlanFeature row is a single flat limit shared by every interval
            // the plan is sold at. Preserve that exactly - assign each existing row to the plan's lowest-value
            // sold BillingInterval (from SubscriptionPlanPrices, global/CountryCode IS NULL rows only - regional
            // price rows never affect quota), then duplicate it across the plan's other sold intervals with the
            // same Limit/FeatureGroupKey/FeatureGroupDescription. Plans with no matching price rows keep the
            // column default (Monthly) and are not duplicated. Zero admin-visible behavior change until someone
            // edits a plan's per-interval limits.
            migrationBuilder.Sql(@"
UPDATE spf
SET spf.BillingInterval = t.MinInterval
FROM SubscriptionPlanFeatures spf
CROSS APPLY (
    SELECT MIN(spp.BillingInterval) AS MinInterval
    FROM SubscriptionPlanPrices spp
    WHERE spp.SubscriptionPlanId = spf.SubscriptionPlanId AND spp.CountryCode IS NULL
) t
WHERE t.MinInterval IS NOT NULL");

            migrationBuilder.Sql(@"
INSERT INTO SubscriptionPlanFeatures (SubscriptionPlanId, FeatureId, BillingInterval, [Limit], FeatureGroupKey, FeatureGroupDescription)
SELECT spf.SubscriptionPlanId, spf.FeatureId, spp.BillingInterval, spf.[Limit], spf.FeatureGroupKey, spf.FeatureGroupDescription
FROM SubscriptionPlanFeatures spf
JOIN SubscriptionPlanPrices spp
    ON spp.SubscriptionPlanId = spf.SubscriptionPlanId AND spp.CountryCode IS NULL AND spp.BillingInterval <> spf.BillingInterval");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_SubscriptionPlanId_FeatureId_BillingInterval",
                table: "SubscriptionPlanFeatures",
                columns: new[] { "SubscriptionPlanId", "FeatureId", "BillingInterval" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanFeatures_SubscriptionPlanId_FeatureId_BillingInterval",
                table: "SubscriptionPlanFeatures");

            // Collapse back to one row per (SubscriptionPlanId, FeatureId) - keep the row with the lowest Id
            // (the original, pre-backfill row; the interval-duplicate rows added above all have higher Ids).
            migrationBuilder.Sql(@"
DELETE spf
FROM SubscriptionPlanFeatures spf
WHERE spf.Id NOT IN (
    SELECT MIN(spf2.Id)
    FROM SubscriptionPlanFeatures spf2
    WHERE spf2.SubscriptionPlanId = spf.SubscriptionPlanId AND spf2.FeatureId = spf.FeatureId
)");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "SubscriptionPlanFeatures");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_SubscriptionPlanId_FeatureId",
                table: "SubscriptionPlanFeatures",
                columns: new[] { "SubscriptionPlanId", "FeatureId" },
                unique: true);
        }
    }
}
