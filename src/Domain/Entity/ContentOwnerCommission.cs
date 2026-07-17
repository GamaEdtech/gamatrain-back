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
    /// One accrual row: the commission owed to a content owner for a single paid download of their
    /// content. Append-only, separate from the points ledger (Transaction) and from subscription
    /// quota entirely - a content owner's commission balance is the sum of their unpaid rows here,
    /// never mixed into ApplicationUser.CurrentBalance or any UserSubscriptionQuota. Payout itself
    /// (crossing ApplicationSettingsDto.ContentOwnerCommissionPayoutThresholdUsd) is a separate,
    /// not-yet-built phase - this entity intentionally carries no paid/payout columns yet.
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

        /// <summary>The user whose download triggered this accrual.</summary>
        [Column(nameof(DownloaderUserId), DataType.Long)]
        [Required]
        public long DownloaderUserId { get; set; }
        public ApplicationUser? Downloader { get; set; }

        /// <summary>What kind of event earned this row - see CommissionReason for why this is separate from Source.</summary>
        [Column(nameof(Reason), DataType.Byte)]
        [Required]
        public CommissionReason Reason { get; set; }

        [Column(nameof(Source), DataType.Byte)]
        [Required]
        public ContentSource Source { get; set; }

        [Column(nameof(ContentType), DataType.Byte)]
        [Required]
        public ContentType ContentType { get; set; }

        /// <summary>The content's id in the external source (e.g. gama-api's test id).</summary>
        [Column(nameof(ExternalContentId), DataType.Long)]
        [Required]
        public long ExternalContentId { get; set; }

        /// <summary>The external source's own file-type discriminator (e.g. gama-api's pdf/word/answer/extra).</summary>
        [Column(nameof(ExternalFileType), DataType.UnicodeString)]
        [StringLength(20)]
        [Required]
        public string? ExternalFileType { get; set; }

        [Column(nameof(ExternalExtraId), DataType.Long)]
        public long? ExternalExtraId { get; set; }

        /// <summary>Snapshot of the external source's reported price (points) for this download, at accrual time.</summary>
        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        /// <summary>Snapshot of ApplicationSettingsDto.ContentOwnerCommissionPercent at accrual time - later admin edits never change already-accrued rows.</summary>
        [Column(nameof(CommissionPercent), DataType.Decimal)]
        [Required]
        public decimal CommissionPercent { get; set; }

        /// <summary>Commission amount in the base reporting currency (USD), locked at accrual time via the fixed points-per-dollar rate.</summary>
        [Column(nameof(AmountUsd), DataType.Decimal)]
        [Required]
        public decimal AmountUsd { get; set; }

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
            _ = builder.HasIndex(t => t.OwnerUserId);
        }
    }
}
