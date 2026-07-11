using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSubscriptionPlanPricingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Point",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "SubscriptionPlans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Currency",
                table: "SubscriptionPlans",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<long>(
                name: "Point",
                table: "SubscriptionPlans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "SubscriptionPlans",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
