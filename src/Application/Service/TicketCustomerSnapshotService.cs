namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Ticket;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class TicketCustomerSnapshotService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<TicketCustomerSnapshotService>> localizer, Lazy<ILogger<TicketCustomerSnapshotService>> logger)
        : LocalizableServiceBase<TicketCustomerSnapshotService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ITicketCustomerSnapshotService
    {
        /// <summary>How far back ended subscriptions and payments are read.</summary>
        internal static readonly TimeSpan LookBack = TimeSpan.FromDays(60);

        /// <summary>A pending payment younger than this is normal (the customer may still be on the checkout page).</summary>
        internal static readonly TimeSpan PendingGrace = TimeSpan.FromHours(1);

        internal static readonly TimeSpan RecentFailure = TimeSpan.FromDays(14);

        private const int MaxPayments = 10;

        public async Task<ResultData<IReadOnlyList<CustomerFindingDto>>> GetFindingsAsync(long? userId, string? email)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                List<CustomerFindingDto> findings = [];

                // A ticket sent while signed out has only the email the sender typed. The reply goes to that address,
                // so what it says reaches the account's owner; the admin is still told how the account was found.
                if (userId is null && !string.IsNullOrWhiteSpace(email))
                {
                    var normalized = email.Trim().ToUpperInvariant();
                    userId = await uow.GetRepository<ApplicationUser>()
                        .GetManyQueryable(t => t.NormalizedEmail == normalized && t.EmailConfirmed)
                        .Select(t => (long?)t.Id)
                        .FirstOrDefaultAsync();
                    if (userId is not null)
                    {
                        findings.Add(new() { Text = "The ticket was sent while signed out; its account was found by the ticket's email, so check it is the same person.", AdminDetail = $"user {userId}", AdminOnly = true });
                    }
                }

                if (userId is null)
                {
                    findings.Add(new() { Text = "No account matches this ticket (sent while signed out, and no account uses its email), so no account data was checked.", AdminOnly = true });
                    return new(OperationResult.Succeeded) { Data = findings };
                }

                var now = DateTimeOffset.UtcNow;
                var since = now - LookBack;

                var subscriptions = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.UserId == userId && (t.Status == UserSubscriptionStatus.Active || t.CreationDate >= since || t.ExpirationDate >= since))
                    .Select(t => new SubscriptionFact
                    {
                        Id = t.Id,
                        Plan = t.SubscriptionPlan!.Title,
                        Status = t.Status,
                        BillingInterval = t.BillingInterval,
                        ExpirationDate = t.ExpirationDate,
                        Recurring = t.ExternalSubscriptionId != null,
                        CancelAtPeriodEnd = t.CancelAtPeriodEnd,
                        PendingSwitchPlan = t.PendingSwitchSubscriptionPlan!.Title,
                        LastPaymentFailedDate = t.LastPaymentFailedDate,
                        SwitchLockedUntil = t.SwitchLockedUntil,
                        Quotas = t.Quotas.Select(q => new QuotaFact
                        {
                            Limit = q.Limit,
                            Used = q.Used,
                            Features = q.Features.Select(f => f.Feature!.Name ?? f.Feature.Code).ToList(),
                        }).ToList(),
                    })
                    .ToListAsync();

                var payments = await uow.GetRepository<Payment>()
                    .GetManyQueryable(t => t.UserId == userId && t.CreationDate >= since)
                    .OrderByDescending(t => t.CreationDate)
                    .Take(MaxPayments)
                    .Select(t => new PaymentFact
                    {
                        Id = t.Id,
                        Status = t.Status,
                        Kind = t.Kind,
                        Gateway = t.Gateway,
                        CreationDate = t.CreationDate,
                    })
                    .ToListAsync();

                findings.AddRange(BuildFindings(subscriptions, payments, now));
                return new(OperationResult.Succeeded) { Data = findings };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>The checks themselves, kept free of the database so they can be tested directly.</summary>
        internal static List<CustomerFindingDto> BuildFindings(IReadOnlyList<SubscriptionFact> subscriptions, IReadOnlyList<PaymentFact> payments, DateTimeOffset now)
        {
            List<CustomerFindingDto> findings = [];

            var active = subscriptions.Where(t => t.Status == UserSubscriptionStatus.Active).OrderByDescending(t => t.ExpirationDate).ToList();
            if (active.Count == 0)
            {
                var last = subscriptions.OrderByDescending(t => t.ExpirationDate).FirstOrDefault();
                if (last is null)
                {
                    findings.Add(new() { Text = $"The account has no subscription in the last {LookBack.Days} days." });
                }
                else
                {
                    var endDate = last.ExpirationDate is null ? string.Empty : $", end date {Day(last.ExpirationDate.Value)}";
                    findings.Add(new()
                    {
                        Text = $"The account has no active subscription. The last one ({last.Plan}, {Interval(last.BillingInterval)}) is {last.Status.Name}{endDate}.",
                        AdminDetail = $"subscription {last.Id}",
                    });
                }
            }

            if (active.Count > 1)
            {
                findings.Add(new()
                {
                    Text = $"The account has {active.Count} active subscriptions at the same time ({string.Join(", ", active.Select(t => t.Plan))}). This may be a double charge: escalate it, don't resolve it in the reply.",
                    AdminDetail = $"subscriptions {string.Join(", ", active.Select(t => t.Id))}",
                });
            }

            foreach (var subscription in active)
            {
                AddSubscriptionFindings(findings, subscription, now);
            }

            AddPaymentFindings(findings, payments, now);

            return findings;
        }

        private static void AddSubscriptionFindings(List<CustomerFindingDto> findings, SubscriptionFact subscription, DateTimeOffset now)
        {
            var detail = $"subscription {subscription.Id}";
            var end = subscription.ExpirationDate is null ? "no end date" : Day(subscription.ExpirationDate.Value);
            var state = (subscription.CancelAtPeriodEnd, subscription.Recurring) switch
            {
                (true, _) => $"cancelled by the customer, stays usable until {end} and won't renew",
                (false, true) => $"renews automatically on {end}",
                _ => $"ends on {end} (doesn't renew automatically)",
            };

            findings.Add(new() { Text = $"Active subscription: {subscription.Plan}, billed {Interval(subscription.BillingInterval)}, {state}.", AdminDetail = detail });

            if (subscription.LastPaymentFailedDate is not null && now - subscription.LastPaymentFailedDate.Value <= RecentFailure)
            {
                findings.Add(new() { Text = $"The last renewal charge for this subscription failed on {Day(subscription.LastPaymentFailedDate.Value)}.", AdminDetail = detail });
            }

            if (subscription.PendingSwitchPlan is not null)
            {
                findings.Add(new() { Text = $"A change to the {subscription.PendingSwitchPlan} plan is scheduled for the end of the current period.", AdminDetail = detail });
            }

            if (subscription.SwitchLockedUntil > now)
            {
                findings.Add(new() { Text = "A plan change for this subscription is being processed right now.", AdminDetail = detail });
            }

            foreach (var quota in subscription.Quotas)
            {
                var names = quota.Features.Count == 0 ? "A feature" : string.Join(" / ", quota.Features);
                var text = quota.Limit switch
                {
                    null => $"{names}: unlimited ({quota.Used} used).",
                    var limit when quota.Used >= limit => $"{names}: allowance used up ({quota.Used} of {limit} used).",
                    var limit => $"{names}: {quota.Used} of {limit} used.",
                };
                findings.Add(new() { Text = text, AdminDetail = detail });
            }
        }

        private static void AddPaymentFindings(List<CustomerFindingDto> findings, IReadOnlyList<PaymentFact> payments, DateTimeOffset now)
        {
            foreach (var payment in payments.Where(t => t.Status == PaymentStatus.Pending && now - t.CreationDate > PendingGrace))
            {
                findings.Add(new()
                {
                    Text = $"A {KindText(payment.Kind)} payment started on {Day(payment.CreationDate)} is still not confirmed. Escalate it: the team checks whether the money was taken.",
                    AdminDetail = Detail(payment),
                });
            }

            foreach (var payment in payments.Where(t => t.Status == PaymentStatus.Failed && now - t.CreationDate <= RecentFailure))
            {
                findings.Add(new() { Text = $"A {KindText(payment.Kind)} payment on {Day(payment.CreationDate)} failed.", AdminDetail = Detail(payment) });
            }

            // Two paid first purchases close together is how the double-billing bug showed up.
            var purchases = payments.Where(t => t.Status == PaymentStatus.Paid && t.Kind == PaymentKind.NewSubscription).OrderBy(t => t.CreationDate).ToList();
            for (var i = 1; i < purchases.Count; i++)
            {
                if (purchases[i].CreationDate - purchases[i - 1].CreationDate <= TimeSpan.FromDays(1))
                {
                    findings.Add(new()
                    {
                        Text = $"Two new-subscription payments were paid within a day of each other ({Day(purchases[i - 1].CreationDate)} and {Day(purchases[i].CreationDate)}). This may be a double charge: escalate it.",
                        AdminDetail = $"payments {purchases[i - 1].Id}, {purchases[i].Id}",
                    });
                    break;
                }
            }

            var lastPaid = payments.Where(t => t.Status == PaymentStatus.Paid).MaxBy(t => t.CreationDate);
            if (lastPaid is not null)
            {
                findings.Add(new() { Text = $"Last successful payment: {KindText(lastPaid.Kind)} on {Day(lastPaid.CreationDate)}.", AdminDetail = Detail(lastPaid) });
            }
            else if (payments.Count == 0)
            {
                findings.Add(new() { Text = $"No payments in the last {LookBack.Days} days." });
            }
        }

        private static string Day(DateTimeOffset date) => date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Detail(PaymentFact payment) => $"payment {payment.Id}, {payment.Gateway.Name}";

        private static string Interval(BillingInterval interval) => interval == BillingInterval.Annual ? "yearly" : "monthly";

        private static string KindText(PaymentKind? kind) => kind?.Name switch
        {
            nameof(PaymentKind.NewSubscription) => "new subscription",
            nameof(PaymentKind.Renewal) => "renewal",
            nameof(PaymentKind.PlanSwitch) => "plan change",
            nameof(PaymentKind.PointsTopUp) => "points top-up",
            _ => "subscription or points",
        };

        internal sealed class SubscriptionFact
        {
            public long Id { get; init; }
            public string? Plan { get; init; }
            public required UserSubscriptionStatus Status { get; init; }
            public required BillingInterval BillingInterval { get; init; }
            public DateTimeOffset? ExpirationDate { get; init; }
            public bool Recurring { get; init; }
            public bool CancelAtPeriodEnd { get; init; }
            public string? PendingSwitchPlan { get; init; }
            public DateTimeOffset? LastPaymentFailedDate { get; init; }
            public DateTimeOffset? SwitchLockedUntil { get; init; }
            public List<QuotaFact> Quotas { get; init; } = [];
        }

        internal sealed class QuotaFact
        {
            public int? Limit { get; init; }
            public int Used { get; init; }
            public List<string?> Features { get; init; } = [];
        }

        internal sealed class PaymentFact
        {
            public long Id { get; init; }
            public required PaymentStatus Status { get; init; }
            public PaymentKind? Kind { get; init; }
            public required PaymentGateway Gateway { get; init; }
            public DateTimeOffset CreationDate { get; init; }
        }
    }
}
