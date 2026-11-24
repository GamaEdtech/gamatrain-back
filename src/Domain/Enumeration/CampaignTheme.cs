namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// The look a sale campaign gives the home hero and the plans dialog while it is live (<c>Campaigns.Theme</c>). The
    /// frontend draws each one; the backend only stores which. Stored: never renumber.
    /// </summary>
    public sealed class CampaignTheme : Enumeration<CampaignTheme, byte>
    {
        /// <summary>Any event: the site's own colours with the campaign's title.</summary>
        [Display]
        public static readonly CampaignTheme Generic = new(nameof(Generic), 0);

        /// <summary>Black and gold.</summary>
        [Display]
        public static readonly CampaignTheme BlackFriday = new(nameof(BlackFriday), 1);

        /// <summary>Snowfall, red and green.</summary>
        [Display]
        public static readonly CampaignTheme Christmas = new(nameof(Christmas), 2);

        /// <summary>Fireworks.</summary>
        [Display]
        public static readonly CampaignTheme NewYear = new(nameof(NewYear), 3);

        public CampaignTheme()
        {
        }

        public CampaignTheme(string name, byte value)
            : base(name, value)
        {
        }
    }
}
