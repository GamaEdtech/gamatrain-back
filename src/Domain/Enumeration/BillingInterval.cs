namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class BillingInterval : Enumeration<BillingInterval, byte>
    {
        [Display]
        public static readonly BillingInterval Daily = new(nameof(Daily), 0, 1);

        [Display]
        public static readonly BillingInterval Weekly = new(nameof(Weekly), 1, 7);

        [Display]
        public static readonly BillingInterval Monthly = new(nameof(Monthly), 2, 30);

        [Display]
        public static readonly BillingInterval Quarterly = new(nameof(Quarterly), 3, 90);

        [Display]
        public static readonly BillingInterval Annual = new(nameof(Annual), 4, 365);

        /// <summary>
        /// Only ever the true, exact interval length for Daily/Weekly, where a "day"/"week" is unambiguous
        /// regardless of the calendar. For Monthly/Quarterly/Annual this is a fixed approximation
        /// (30/90/365) - never used by <see cref="CalculateEndDate"/> for those any more (fixed 2026-09-14,
        /// see that method's own doc comment); kept only for whatever still reads it directly (e.g. display).
        /// </summary>
        public int Days { get; }

        public BillingInterval()
        {
        }

        public BillingInterval(string name, byte value, int days) : base(name, value) => Days = days;

        /// <summary>
        /// Fixed 2026-09-14, live-reported: a subscriber saw Stripe's own next-invoice date (a real calendar
        /// month/year later) disagree with this app's ExpirationDate by 1-3 days. The old implementation
        /// (<c>start.AddDays(Days)</c>) used a fixed day-count per interval - correct for Daily/Weekly (a
        /// day/week is unambiguous), but Monthly/Quarterly/Annual don't have a fixed length: Stripe bills on
        /// real calendar months/years (Jan 31 -&gt; Feb 28/29, clamped; a leap year is 366 days), so 30/90/365
        /// fixed days drifts depending on which specific months/years are spanned. Now uses true calendar
        /// arithmetic instead, matching how Stripe itself computes a billing cycle: <see
        /// cref="DateTimeOffset.AddMonths"/>/<see cref="DateTimeOffset.AddYears"/> clamp an overflowing day
        /// (e.g. Jan 31 + 1 month) to the target month's last valid day - the same clamping behavior Stripe
        /// documents for its own billing-cycle anchor day. This is still only ever a local approximation for
        /// the very first period (<c>ActivateSubscriptionAsync</c>) or a fallback if the gateway didn't report
        /// a period end (<c>RenewSubscriptionAsync</c>'s <c>gatewayPeriodEnd</c> parameter) - every ordinary
        /// renewal now uses Stripe's own reported period end directly instead of calling this at all.
        /// </summary>
        public DateTimeOffset CalculateEndDate(DateTimeOffset start) => this switch
        {
            _ when this == Monthly => start.AddMonths(1),
            _ when this == Quarterly => start.AddMonths(3),
            _ when this == Annual => start.AddYears(1),
            _ => start.AddDays(Days),
        };
    }
}
