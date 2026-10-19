using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutRecoveryOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutRecoveryOffers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AbandonedPaymentId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionPlanId = table.Column<long>(type: "bigint", nullable: false),
                    BillingInterval = table.Column<byte>(type: "tinyint", nullable: false),
                    DiscountPercent = table.Column<int>(type: "int", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaymentId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutRecoveryOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutRecoveryOffers_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutRecoveryOffers_Payments_AbandonedPaymentId",
                        column: x => x.AbandonedPaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutRecoveryOffers_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CheckoutRecoveryOffers_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRecoveryOffers_AbandonedPaymentId",
                table: "CheckoutRecoveryOffers",
                column: "AbandonedPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRecoveryOffers_PaymentId",
                table: "CheckoutRecoveryOffers",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRecoveryOffers_SubscriptionPlanId",
                table: "CheckoutRecoveryOffers",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRecoveryOffers_UserId",
                table: "CheckoutRecoveryOffers",
                column: "UserId");

            // The checkout recovery email (NudgeType.CheckoutAbandoned = 6), admin-editable like the other nudge templates.
            // Seeded inactive: it sends discounts to real customers, so an admin turns it on after checking the discount
            // (ApplicationSettings) and the wording. CtaUrl is the offer page; the per-user token is appended to it.
            migrationBuilder.InsertData(
                table: "NudgeTemplates",
                columns: new[] { "NudgeType", "Subject", "Body", "CtaLabel", "CtaUrl", "IsActive", "CreationDate" },
                values: new object[]
                {
                    (byte)6,
                    "Your [PLAN_TITLE] plan is still waiting - [DISCOUNT]% off",
                    "Hi [RECEIVER_NAME],<br><br>You started signing up for <b>[PLAN_TITLE]</b> but didn't finish. We saved it for you, and if you complete it by <b>[EXPIRY_DATE]</b> your first payment is <b>[DISCOUNT]% off</b>.<br><br><a href=\"[CTA_URL]\">Finish my subscription</a><br><br>The discount applies to your first payment only and can be used once.",
                    "Finish my subscription",
                    "https://gamatrain.com/offer",
                    false,
                    DateTimeOffset.UtcNow,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "NudgeTemplates",
                keyColumn: "NudgeType",
                keyValue: (byte)6);

            migrationBuilder.DropTable(
                name: "CheckoutRecoveryOffers");
        }
    }
}
