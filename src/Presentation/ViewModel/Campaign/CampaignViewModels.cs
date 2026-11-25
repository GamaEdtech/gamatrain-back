namespace GamaEdtech.Presentation.ViewModel.Campaign
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Dates in the campaign view models are ISO 8601 UTC strings ("2026-11-27T00:00:00.0000000Z"), not DateTimeOffset:
    /// the API's shared date converter writes "MM/dd/yyyy HH:mm:ss" shifted to the caller's time zone with no zone in it,
    /// and the site's countdown and its switch on and off at the exact start and end need the real instant.
    /// </summary>
    public static class CampaignDate
    {
        public static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        public static string? Iso(DateTimeOffset? value) => value.HasValue ? Iso(value.Value) : null;
    }

    /// <summary>
    /// The campaign the site shows (<c>campaigns/current</c>): live, or enabled and starting within a week
    /// (<see cref="IsLive"/> false), so an open page switches on at <see cref="StartsAt"/> and off at <see cref="EndsAt"/>
    /// by itself. No prices: those come with the plans, for the signed-in caller.
    /// </summary>
    public sealed class PublicCampaignViewModel
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Headline { get; set; }

        [JsonConverter(typeof(EnumerationConverter<CampaignTheme, byte>))]
        public CampaignTheme? Theme { get; set; }

        public int DiscountPercent { get; set; }
        public string? StartsAt { get; set; }
        public string? EndsAt { get; set; }
        public bool IsLive { get; set; }
    }

    public sealed class CampaignViewModel
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Headline { get; set; }

        [JsonConverter(typeof(EnumerationConverter<CampaignTheme, byte>))]
        public CampaignTheme? Theme { get; set; }

        public int DiscountPercent { get; set; }

        /// <summary>The dates as entered (a yearly campaign's first run).</summary>
        public string? StartsAt { get; set; }
        public string? EndsAt { get; set; }
        public bool RepeatsYearly { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>The run that is live now or comes next; null when a one-off campaign is over.</summary>
        public string? CurrentStartsAt { get; set; }
        public string? CurrentEndsAt { get; set; }
        public bool IsLive { get; set; }

        /// <summary>Paid first subscription payments that got this campaign's discount.</summary>
        public int PaidPaymentCount { get; set; }

        public string? CreationDate { get; set; }
        public string? LastModifyDate { get; set; }
    }

    public sealed class CampaignListViewModel
    {
        public int Total { get; set; }
        public IEnumerable<CampaignViewModel> Items { get; set; } = [];
    }

    /// <summary>Create or edit a campaign. A new one starts disabled; enabling is its own call.</summary>
    public sealed class ManageCampaignRequestViewModel
    {
        /// <summary>1 to 60 characters, shown to users ("Black Friday").</summary>
        public string? Title { get; set; }

        /// <summary>Optional hero line, at most 200 characters.</summary>
        public string? Headline { get; set; }

        /// <summary>Generic, BlackFriday, Christmas or NewYear (name or value).</summary>
        public string? Theme { get; set; }

        /// <summary>1 to 90.</summary>
        public int? DiscountPercent { get; set; }

        public DateTimeOffset? StartsAt { get; set; }

        /// <summary>Exclusive. A yearly campaign lasts at most 180 days.</summary>
        public DateTimeOffset? EndsAt { get; set; }

        /// <summary>Comes back every year on the same days until disabled.</summary>
        public bool RepeatsYearly { get; set; }

        /// <summary>The caller's authenticator code: needed only when editing the percent, dates or repeat of an enabled campaign.</summary>
        public string? TwoFactorCode { get; set; }
    }

    public sealed class SetCampaignEnabledRequestViewModel
    {
        public bool Enabled { get; set; }

        /// <summary>The caller's authenticator code: needed to enable, not to disable.</summary>
        public string? TwoFactorCode { get; set; }
    }
}
