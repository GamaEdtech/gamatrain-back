namespace GamaEdtech.Presentation.ViewModel.Campaign
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

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
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
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
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public bool RepeatsYearly { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>The run that is live now or comes next; null when a one-off campaign is over.</summary>
        public DateTimeOffset? CurrentStartsAt { get; set; }
        public DateTimeOffset? CurrentEndsAt { get; set; }
        public bool IsLive { get; set; }

        /// <summary>Paid first subscription payments that got this campaign's discount.</summary>
        public int PaidPaymentCount { get; set; }

        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset? LastModifyDate { get; set; }
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
