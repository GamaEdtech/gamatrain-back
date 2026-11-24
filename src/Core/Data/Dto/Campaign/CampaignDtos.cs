namespace GamaEdtech.Data.Dto.Campaign
{
    using System;
    using System.Collections.Generic;

    using GamaEdtech.Domain.Enumeration;

    /// <summary>A sale campaign as the admin sees it.</summary>
    public sealed class CampaignDto
    {
        public long Id { get; set; }
        public required string Title { get; set; }
        public string? Headline { get; set; }
        public required CampaignTheme Theme { get; set; }
        public int DiscountPercent { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public bool RepeatsYearly { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>The run that is live now or comes next (a yearly campaign's this-year or next-year dates); null when a
        /// one-off campaign is over.</summary>
        public DateTimeOffset? CurrentStartsAt { get; set; }
        public DateTimeOffset? CurrentEndsAt { get; set; }

        /// <summary>Enabled and inside its current run.</summary>
        public bool IsLive { get; set; }

        /// <summary>Paid first subscription payments that got this campaign's discount.</summary>
        public int PaidPaymentCount { get; set; }

        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset? LastModifyDate { get; set; }
    }

    public sealed class CampaignListDto
    {
        public int Total { get; set; }
        public IReadOnlyList<CampaignDto> Items { get; set; } = [];
    }

    /// <summary>Create (no <see cref="Id"/>) or edit a campaign. Never enables it: that is <c>SetEnabledAsync</c>.</summary>
    public sealed class ManageCampaignRequestDto
    {
        public long? Id { get; set; }
        public string? Title { get; set; }
        public string? Headline { get; set; }
        public CampaignTheme? Theme { get; set; }
        public int? DiscountPercent { get; set; }
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public bool RepeatsYearly { get; set; }
        public required long UserId { get; set; }

        /// <summary>Needed only when the campaign is enabled and the percent or dates change.</summary>
        public string? TwoFactorCode { get; set; }
    }

    /// <summary>
    /// What the site needs to show a campaign: no prices. <see cref="StartsAt"/>/<see cref="EndsAt"/> are the current run's
    /// (a yearly campaign's this-year dates). <see cref="IsLive"/> false means it is enabled and starts within
    /// the look-ahead window, so the page can switch on at <see cref="StartsAt"/> by itself.
    /// </summary>
    public sealed class PublicCampaignDto
    {
        public long Id { get; set; }
        public required string Title { get; set; }
        public string? Headline { get; set; }
        public required CampaignTheme Theme { get; set; }
        public int DiscountPercent { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public bool IsLive { get; set; }
    }
}
