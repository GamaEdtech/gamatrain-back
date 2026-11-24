namespace GamaEdtech.Data.Dto.Campaign
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Sale campaign rules (2026-11-24), the one place they live, so the checkout, the prices shown and the admin checks
    /// agree. See docs/business/campaigns.md.
    /// </summary>
    public static class CampaignRules
    {
        public const int MinDiscountPercent = 1;
        public const int MaxDiscountPercent = 90;
        public const int MaxTitleLength = 60;
        public const int MaxHeadlineLength = 200;

        /// <summary>A yearly campaign must be shorter than this, so one year's run ends well before the next starts.</summary>
        public static readonly TimeSpan MaxYearlyLength = TimeSpan.FromDays(180);

        /// <summary>How far ahead <c>campaigns/current</c> announces an enabled campaign that hasn't started, so a page
        /// already open can switch on at the start by itself.</summary>
        public static readonly TimeSpan LookAhead = TimeSpan.FromDays(7);

        /// <summary>
        /// The run that is live at <paramref name="now"/> or comes next: the stored window, or for a yearly campaign that
        /// window moved by the fewest whole years that leaves its end after <paramref name="now"/>. Null when a one-off
        /// campaign is over.
        /// </summary>
        public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt)? Occurrence(DateTimeOffset startsAt, DateTimeOffset endsAt,
            bool repeatsYearly, DateTimeOffset now)
        {
            if (endsAt > now)
            {
                return (startsAt, endsAt);
            }

            if (!repeatsYearly)
            {
                return null;
            }

            var years = Math.Max(1, now.Year - endsAt.Year);
            while (endsAt.AddYears(years) <= now)
            {
                years++;
            }

            return (startsAt.AddYears(years), endsAt.AddYears(years));
        }

        /// <summary>Live: enabled and <paramref name="now"/> inside its current run, [start, end).</summary>
        public static bool IsLive(bool isEnabled, DateTimeOffset startsAt, DateTimeOffset endsAt, bool repeatsYearly, DateTimeOffset now) =>
            isEnabled && Occurrence(startsAt, endsAt, repeatsYearly, now) is { } run && run.StartsAt <= now;

        /// <summary>Two [start, end) windows share an instant.</summary>
        public static bool Overlaps(DateTimeOffset startsA, DateTimeOffset endsA, DateTimeOffset startsB, DateTimeOffset endsB) =>
            startsA < endsB && startsB < endsA;

        /// <summary>
        /// Whether two campaigns would ever be live at once from <paramref name="now"/> on, yearly runs included. Runs are
        /// compared up to two years past the later stored end, which covers every pattern: yearly ones repeat after that.
        /// </summary>
        public static bool OverlapsFrom((DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool RepeatsYearly) a,
            (DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool RepeatsYearly) b, DateTimeOffset now)
        {
            var laterEnd = a.EndsAt > b.EndsAt ? a.EndsAt : b.EndsAt;
            var until = (laterEnd > now ? laterEnd : now).AddYears(2);
            var runsB = Runs(b, now, until).ToList();
            return Runs(a, now, until).Any(ra => runsB.Any(rb => Overlaps(ra.StartsAt, ra.EndsAt, rb.StartsAt, rb.EndsAt)));
        }

        /// <summary>The first problem with a campaign's fields (reference, message), or null when it is valid.</summary>
        public static (string Reference, string Message)? Validate(string? title, string? headline, int? discountPercent,
            DateTimeOffset? startsAt, DateTimeOffset? endsAt, bool repeatsYearly)
        {
            var trimmed = title?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxTitleLength)
            {
                return ("title", $"The title is 1 to {MaxTitleLength} characters.");
            }

            if (headline?.Trim().Length > MaxHeadlineLength)
            {
                return ("headline", $"The headline is at most {MaxHeadlineLength} characters.");
            }

            if (discountPercent is not (>= MinDiscountPercent and <= MaxDiscountPercent))
            {
                return ("discountPercent", $"The discount is {MinDiscountPercent} to {MaxDiscountPercent} percent.");
            }

            if (startsAt is null || endsAt is null || endsAt <= startsAt)
            {
                return ("endsAt", "The campaign must end after it starts.");
            }

            return repeatsYearly && endsAt - startsAt > MaxYearlyLength
                ? ("endsAt", $"A yearly campaign lasts at most {MaxYearlyLength.TotalDays} days.")
                : null;
        }

        /// <summary>
        /// The discount a first payment gets when a checkout recovery offer and a live campaign both apply: the larger one,
        /// never both. <c>FromCampaign</c> is true when the campaign's won.
        /// </summary>
        public static (int? Percent, bool FromCampaign) PickDiscount(int? recoveryPercent, int? campaignPercent) =>
            campaignPercent is int campaign && campaign > (recoveryPercent ?? 0)
                ? (campaign, true)
                : (recoveryPercent, false);

        private static IEnumerable<(DateTimeOffset StartsAt, DateTimeOffset EndsAt)> Runs((DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool RepeatsYearly) campaign,
            DateTimeOffset from, DateTimeOffset until)
        {
            for (var years = 0; campaign.StartsAt.AddYears(years) < until; years++)
            {
                var run = (StartsAt: campaign.StartsAt.AddYears(years), EndsAt: campaign.EndsAt.AddYears(years));
                if (run.EndsAt > from)
                {
                    yield return run;
                }

                if (!campaign.RepeatsYearly)
                {
                    yield break;
                }
            }
        }
    }
}
