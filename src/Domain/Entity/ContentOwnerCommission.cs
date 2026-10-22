namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// One accrual row: commission owed to a user (OwnerUserId) for one event - a paid download of their
    /// content (CommissionReason.ContentDownload), or since 2026-10-21 a subscription payment by a user they
    /// invited (CommissionReason.ReferralSubscription, see docs/business/referrals.md). Append-only, separate from the points ledger (Transaction) and from subscription
    /// quota entirely - a content owner's commission balance is the sum of their unpaid rows here,
    /// never mixed into ApplicationUser.CurrentBalance or any UserSubscriptionQuota. Payouts are a
    /// separate entity (CommissionPayout, 2026-10-07): rows here are never marked paid; the available
    /// balance is the sum here minus the owner's Pending/Approved/Paid payouts.
    /// </summary>
    [Table(nameof(ContentOwnerCommission))]
    public class ContentOwnerCommission : IEntity<ContentOwnerCommission, long>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        /// <summary>The content owner/uploader, resolved from the external source's owner id (e.g. gama-api's CoreId).</summary>
        [Column(nameof(OwnerUserId), DataType.Long)]
        [Required]
        public long OwnerUserId { get; set; }
        public ApplicationUser? Owner { get; set; }

        /// <summary>The user whose action earned this row: the downloader, or for a referral the invited user who paid.</summary>
        [Column(nameof(DownloaderUserId), DataType.Long)]
        [Required]
        public long DownloaderUserId { get; set; }
        public ApplicationUser? Downloader { get; set; }

        /// <summary>What kind of event earned this row - see CommissionReason for why this is separate from Source.</summary>
        [Column(nameof(Reason), DataType.Byte)]
        [Required]
        public CommissionReason Reason { get; set; }

        /// <summary>Download rows only (null for a referral row).</summary>
        [Column(nameof(Source), DataType.Byte)]
        public ContentSource? Source { get; set; }

        /// <summary>Download rows only (null for a referral row).</summary>
        [Column(nameof(ContentType), DataType.Byte)]
        public ContentType? ContentType { get; set; }

        /// <summary>The content's id in the external source (e.g. gama-api's test id). Download rows only.</summary>
        [Column(nameof(ExternalContentId), DataType.Long)]
        public long? ExternalContentId { get; set; }

        /// <summary>The external source's own file-type discriminator (e.g. gama-api's pdf/word/answer/extra). Download rows only.</summary>
        [Column(nameof(ExternalFileType), DataType.UnicodeString)]
        [StringLength(20)]
        public string? ExternalFileType { get; set; }

        [Column(nameof(ExternalExtraId), DataType.Long)]
        public long? ExternalExtraId { get; set; }

        /// <summary>Snapshot of the external source's reported price (points) for this download, at accrual time. 0 for a referral row.</summary>
        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        /// <summary>
        /// The percent applied to this row. Download: snapshot of ApplicationSettingsDto.ContentOwnerCommissionPercent at accrual
        /// time. Referral: the inviting teacher's locked ReferralMembership.Percent. Later admin edits never change a row.
        /// </summary>
        [Column(nameof(CommissionPercent), DataType.Decimal)]
        [Required]
        public decimal CommissionPercent { get; set; }

        /// <summary>Commission amount in the base reporting currency (USD), locked at accrual time via the fixed points-per-dollar rate.</summary>
        [Column(nameof(AmountUsd), DataType.Decimal)]
        [Required]
        public decimal AmountUsd { get; set; }

        /// <summary>Referral rows only: the subscription payment this share was taken from. Unique, so a payment can never pay commission twice.</summary>
        [Column(nameof(PaymentId), DataType.Long)]
        public long? PaymentId { get; set; }
        public Payment? Payment { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ContentOwnerCommission> builder)
        {
            _ = builder.Property(t => t.CommissionPercent).HasPrecision(5, 2);
            _ = builder.Property(t => t.AmountUsd).HasPrecision(18, 4);
            _ = builder.OwnEnumeration<ContentOwnerCommission, CommissionReason, byte>(t => t.Reason);
            _ = builder.OwnEnumeration<ContentOwnerCommission, ContentSource, byte>(t => t.Source);
            _ = builder.OwnEnumeration<ContentOwnerCommission, ContentType, byte>(t => t.ContentType);
            _ = builder.HasOne(t => t.Owner).WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.Downloader).WithMany().HasForeignKey(t => t.DownloaderUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.Payment).WithMany().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => t.PaymentId).IsUnique().HasFilter("[PaymentId] IS NOT NULL");
            // Composite, OwnerUserId leading - a strict superset of the old (OwnerUserId) index, so it
            // still serves every existing owner-only lookup (GetContentOwnerCommissionsAsync) while also
            // covering GetCommissionStatisticsAsync's OwnerUserId + CreationDate range scan without a
            // second index or a table scan for the date filter.
            _ = builder.HasIndex(t => new { t.OwnerUserId, t.CreationDate });
        }
    }
}
