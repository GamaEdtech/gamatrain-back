namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A time-boxed sale (Black Friday, Christmas, New Year...; 2026-11-24): while it is enabled and between
    /// <see cref="StartsAt"/> and <see cref="EndsAt"/>, a new subscription's first payment is <see cref="DiscountPercent"/>
    /// off, and the site shows the campaign's <see cref="Theme"/>. The server decides the discount at checkout, never the
    /// client. See docs/business/campaigns.md.
    /// </summary>
    [Table(nameof(Campaign))]
    public class Campaign : IEntity<Campaign, long>
    {
        public const int MinDiscountPercent = 1;
        public const int MaxDiscountPercent = 90;
        public const int MaxTitleLength = 60;
        public const int MaxHeadlineLength = 200;

        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        /// <summary>Shown to users ("Black Friday") and in the Stripe coupon's name.</summary>
        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(MaxTitleLength)]
        [Required]
        public string Title { get; set; } = string.Empty;

        /// <summary>Optional line for the home hero; the theme's own text when empty.</summary>
        [Column(nameof(Headline), DataType.UnicodeString)]
        [StringLength(MaxHeadlineLength)]
        public string? Headline { get; set; }

        [Column(nameof(Theme), DataType.Byte)]
        [Required]
        public CampaignTheme Theme { get; set; } = CampaignTheme.Generic;

        /// <summary>Percent off a new subscription's first payment, 1 to 90.</summary>
        [Column(nameof(DiscountPercent), DataType.Int)]
        [Required]
        public int DiscountPercent { get; set; }

        [Column(nameof(StartsAt), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset StartsAt { get; set; }

        /// <summary>Exclusive: the campaign is over at this instant.</summary>
        [Column(nameof(EndsAt), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset EndsAt { get; set; }

        /// <summary>
        /// Comes back every year on the same days (<see cref="StartsAt"/>/<see cref="EndsAt"/> moved by whole years) until
        /// disabled. The stored dates stay the first run's; the current run is worked out on read (CampaignRules.Occurrence).
        /// </summary>
        [Column(nameof(RepeatsYearly), DataType.Boolean)]
        [Required]
        public bool RepeatsYearly { get; set; }

        /// <summary>The admin's one-click switch. A disabled campaign never applies, whatever its dates.</summary>
        [Column(nameof(IsEnabled), DataType.Boolean)]
        [Required]
        public bool IsEnabled { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(CreationUserId), DataType.Long)]
        [Required]
        public long CreationUserId { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        [Column(nameof(LastModifyUserId), DataType.Long)]
        public long? LastModifyUserId { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Campaign> builder)
        {
            _ = builder.OwnEnumeration<Campaign, CampaignTheme, byte>(t => t.Theme);
            _ = builder.HasIndex(t => new { t.IsEnabled, t.EndsAt });
        }
    }
}
