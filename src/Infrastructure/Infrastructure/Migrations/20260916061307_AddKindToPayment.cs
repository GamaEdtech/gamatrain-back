using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKindToPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                table: "Payments",
                type: "tinyint",
                nullable: true);

            // Backfill only what's provably correct from stored data, not a best-effort guess for the rest -
            // see Payment.Kind's own doc comment. A points top-up never has a UserSubscriptionId, and the
            // Checkout Session id (cs_...) TransactionId scheme was, before 2026-09-15's fix, only ever used
            // for a subscription's very first period (PaymentService.VerifyAsync) - both fully reliable.
            // Renewal vs. PlanSwitch can't be told apart from stored data alone for existing rows (both share
            // the Stripe invoice id, in_..., TransactionId scheme prior to that fix), so those - and any
            // subscription-linked row recorded before the cs_/in_ distinction existed at all - are deliberately
            // left NULL rather than guessed.
            migrationBuilder.Sql(@"
UPDATE Payments SET Kind = 0 WHERE UserSubscriptionId IS NULL AND Kind IS NULL;
UPDATE Payments SET Kind = 1 WHERE UserSubscriptionId IS NOT NULL AND TransactionId LIKE 'cs\_%' ESCAPE '\' AND Kind IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Payments");
        }
    }
}
