using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureQuotaGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeatureGroupKey",
                table: "SubscriptionPlanFeatures",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserSubscriptionQuotaFeatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserSubscriptionQuotaId = table.Column<long>(type: "bigint", nullable: false),
                    UserSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptionQuotaFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptionQuotaFeatures_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSubscriptionQuotaFeatures_UserSubscriptionQuotas_UserSubscriptionQuotaId",
                        column: x => x.UserSubscriptionQuotaId,
                        principalTable: "UserSubscriptionQuotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotaFeatures_FeatureId",
                table: "UserSubscriptionQuotaFeatures",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotaFeatures_UserSubscriptionId_FeatureId",
                table: "UserSubscriptionQuotaFeatures",
                columns: new[] { "UserSubscriptionId", "FeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotaFeatures_UserSubscriptionQuotaId",
                table: "UserSubscriptionQuotaFeatures",
                column: "UserSubscriptionQuotaId");

            // Backfill: every existing quota row today covers exactly one feature (grouping didn't exist yet),
            // so this is exact, not a guess. Runs while UserSubscriptionQuotas.FeatureId still exists (dropped below).
            migrationBuilder.Sql(@"
INSERT INTO UserSubscriptionQuotaFeatures (UserSubscriptionQuotaId, UserSubscriptionId, FeatureId)
SELECT Id, UserSubscriptionId, FeatureId FROM UserSubscriptionQuotas");

            migrationBuilder.DropForeignKey(
                name: "FK_UserSubscriptionQuotas_Features_FeatureId",
                table: "UserSubscriptionQuotas");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptionQuotas_FeatureId",
                table: "UserSubscriptionQuotas");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptionQuotas_UserSubscriptionId_FeatureId",
                table: "UserSubscriptionQuotas");

            migrationBuilder.DropColumn(
                name: "FeatureId",
                table: "UserSubscriptionQuotas");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotas_UserSubscriptionId",
                table: "UserSubscriptionQuotas",
                column: "UserSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSubscriptionQuotaFeatures");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptionQuotas_UserSubscriptionId",
                table: "UserSubscriptionQuotas");

            migrationBuilder.DropColumn(
                name: "FeatureGroupKey",
                table: "SubscriptionPlanFeatures");

            migrationBuilder.AddColumn<int>(
                name: "FeatureId",
                table: "UserSubscriptionQuotas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotas_FeatureId",
                table: "UserSubscriptionQuotas",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotas_UserSubscriptionId_FeatureId",
                table: "UserSubscriptionQuotas",
                columns: new[] { "UserSubscriptionId", "FeatureId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserSubscriptionQuotas_Features_FeatureId",
                table: "UserSubscriptionQuotas",
                column: "FeatureId",
                principalTable: "Features",
                principalColumn: "Id");
        }
    }
}
