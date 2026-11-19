namespace GamaEdtech.Test.Application
{
    using System;
    using System.Linq;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Ticket;
    using GamaEdtech.Domain.Enumeration;

    using Xunit;

    using static GamaEdtech.Application.Service.TicketCustomerSnapshotService;

    public class TicketCustomerSnapshotServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        private static SubscriptionFact Active(long id = 1, string plan = "Pro") => new()
        {
            Id = id,
            Plan = plan,
            Status = UserSubscriptionStatus.Active,
            BillingInterval = BillingInterval.Monthly,
            ExpirationDate = Now.AddDays(10),
            Recurring = true,
        };

        private static PaymentFact Payment(long id, PaymentStatus status, PaymentKind kind, DateTimeOffset date) => new()
        {
            Id = id,
            Status = status,
            Kind = kind,
            Gateway = PaymentGateway.Stripe,
            CreationDate = date,
        };

        private static string[] Texts(SubscriptionFact[] subscriptions, PaymentFact[] payments) =>
            [.. TicketCustomerSnapshotService.BuildFindings(subscriptions, payments, Now).Select(t => t.Text)];

        [Fact]
        public void ActiveSubscriptionIsDescribedWithoutIds()
        {
            var findings = TicketCustomerSnapshotService.BuildFindings([Active(id: 42)], [], Now);

            var subscription = Assert.Single(findings, t => t.Text.StartsWith("Active subscription", StringComparison.Ordinal));
            Assert.Equal("Active subscription: Pro, billed monthly, renews automatically on 2026-11-29.", subscription.Text);
            Assert.Equal("subscription 42", subscription.AdminDetail);
            Assert.DoesNotContain(findings, t => t.Text.Contains("42", StringComparison.Ordinal));
        }

        [Fact]
        public void CancelledSubscriptionSaysItWontRenew()
        {
            var subscription = Active();
            subscription = new()
            {
                Id = subscription.Id,
                Plan = subscription.Plan,
                Status = subscription.Status,
                BillingInterval = BillingInterval.Annual,
                ExpirationDate = subscription.ExpirationDate,
                Recurring = true,
                CancelAtPeriodEnd = true,
            };

            Assert.Contains("Active subscription: Pro, billed yearly, cancelled by the customer, stays usable until 2026-11-29 and won't renew.", Texts([subscription], []));
        }

        [Fact]
        public void TwoActiveSubscriptionsAreFlaggedForEscalation()
        {
            var findings = TicketCustomerSnapshotService.BuildFindings([Active(1, "Pro"), Active(2, "Basic")], [], Now);

            var duplicate = Assert.Single(findings, t => t.Text.StartsWith("The account has 2 active subscriptions", StringComparison.Ordinal));
            Assert.Contains("escalate", duplicate.Text, StringComparison.Ordinal);
            Assert.Equal("subscriptions 1, 2", duplicate.AdminDetail);
        }

        [Fact]
        public void NoSubscriptionIsSaid() =>
            Assert.Contains("The account has no subscription in the last 60 days.", Texts([], []));

        [Fact]
        public void ExpiredSubscriptionIsTheLastOne()
        {
            SubscriptionFact expired = new()
            {
                Id = 5,
                Plan = "Pro",
                Status = UserSubscriptionStatus.Expired,
                BillingInterval = BillingInterval.Monthly,
                ExpirationDate = Now.AddDays(-3),
            };

            Assert.Contains("The account has no active subscription. The last one (Pro, monthly) is Expired, end date 2026-11-16.", Texts([expired], []));
        }

        [Fact]
        public void QuotaShowsUsageAndUsedUp()
        {
            var subscription = Active();
            subscription.Quotas.AddRange(
            [
                new() { Limit = 10, Used = 10, Features = ["Exam download"] },
                new() { Limit = 10, Used = 3, Features = ["Paper download", "Tutorial download"] },
                new() { Limit = null, Used = 7, Features = ["AI question"] },
            ]);

            var texts = Texts([subscription], []);

            Assert.Contains("Exam download: allowance used up (10 of 10 used).", texts);
            Assert.Contains("Paper download / Tutorial download: 3 of 10 used.", texts);
            Assert.Contains("AI question: unlimited (7 used).", texts);
        }

        [Fact]
        public void PendingPaymentIsFlaggedOnlyAfterTheGracePeriod()
        {
            var texts = Texts([],
            [
                Payment(1, PaymentStatus.Pending, PaymentKind.Renewal, Now.AddDays(-2)),
                Payment(2, PaymentStatus.Pending, PaymentKind.NewSubscription, Now.AddMinutes(-10)),
            ]);

            _ = Assert.Single(texts, t => t.Contains("still not confirmed", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("A renewal payment started on 2026-11-17 is still not confirmed.", StringComparison.Ordinal));
        }

        [Fact]
        public void RecentFailedPaymentIsReported()
        {
            var findings = TicketCustomerSnapshotService.BuildFindings([],
            [
                Payment(7, PaymentStatus.Failed, PaymentKind.PlanSwitch, Now.AddDays(-1)),
                Payment(8, PaymentStatus.Failed, PaymentKind.PlanSwitch, Now.AddDays(-30)),
            ], Now);

            var failed = Assert.Single(findings, t => t.Text.Contains("failed", StringComparison.Ordinal));
            Assert.Equal("A plan change payment on 2026-11-18 failed.", failed.Text);
            Assert.Equal("payment 7, Stripe", failed.AdminDetail);
        }

        [Fact]
        public void TwoPaidPurchasesWithinADayAreFlagged()
        {
            var texts = Texts([],
            [
                Payment(1, PaymentStatus.Paid, PaymentKind.NewSubscription, Now.AddHours(-5)),
                Payment(2, PaymentStatus.Paid, PaymentKind.NewSubscription, Now.AddHours(-4)),
            ]);

            Assert.Contains(texts, t => t.StartsWith("Two new-subscription payments were paid within a day", StringComparison.Ordinal));
            Assert.Contains("Last successful payment: new subscription on 2026-11-19.", texts);
        }

        [Fact]
        public void NoPaymentsIsSaid() => Assert.Contains("No payments in the last 60 days.", Texts([Active()], []));

        [Fact]
        public void ModelContextHasNoAdminDetailOrAdminOnlyFindings()
        {
            var lines = TicketAiReplyService.BuildFindingsContext(
            [
                new CustomerFindingDto { Text = "A renewal payment failed.", AdminDetail = "payment 99, Stripe" },
                new CustomerFindingDto { Text = "Found by the ticket's email.", AdminDetail = "user 5", AdminOnly = true },
            ]).ToList();
            var context = string.Join('\n', lines);

            Assert.Contains("- A renewal payment failed.", lines);
            Assert.DoesNotContain("payment 99", context, StringComparison.Ordinal);
            Assert.DoesNotContain("user 5", context, StringComparison.Ordinal);
            Assert.DoesNotContain("ticket's email", context, StringComparison.Ordinal);
        }

        [Fact]
        public void ModelContextSaysWhenThereAreNoFindings() =>
            Assert.Contains("None: the ticket isn't linked to an account.", TicketAiReplyService.BuildFindingsContext([]));

        [Fact]
        public void AdminNoteAddsTheDetail()
        {
            Assert.Equal("A payment failed. (payment 1, Stripe)", new CustomerFindingDto { Text = "A payment failed.", AdminDetail = "payment 1, Stripe" }.ToAdminNote());
            Assert.Equal("No payments.", new CustomerFindingDto { Text = "No payments." }.ToAdminNote());
        }
    }
}
