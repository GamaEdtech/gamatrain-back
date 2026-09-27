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
    /// A user's one-time purchase of a piece of content (2026-09-27, generalized from ExamExportPurchase): after it,
    /// that content -- in that variant -- is theirs, free from then on. Which content is <see cref="ContentType"/> +
    /// <see cref="ContentId"/> (+ <see cref="Variant"/> when the same content is sold in several forms, e.g. an exam
    /// export's format). The unique index is what makes "pay once" hold under concurrency: a duplicate first purchase
    /// fails its insert, and that request refunds its own charge (see IContentPurchaseService).
    /// </summary>
    [Table(nameof(ContentPurchase))]
    public class ContentPurchase : IEntity<ContentPurchase, long>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(ContentType), DataType.Byte)]
        [Required]
        public PurchasableContentType ContentType { get; set; }

        /// <summary>The content's id in its own source (a gama-api exam id for <see cref="PurchasableContentType.ExamExport"/>).</summary>
        [Column(nameof(ContentId), DataType.Long)]
        [Required]
        public long ContentId { get; set; }

        /// <summary>Which form of the content was bought (an exam export's format: Pdf/Word/PowerPoint); an empty string
        /// when the content has only one -- never null, so the unique index treats "no variant" as one value.</summary>
        [Column(nameof(Variant), DataType.UnicodeString)]
        [StringLength(50)]
        [Required]
        public string Variant { get; set; } = string.Empty;

        /// <summary>What was charged.</summary>
        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        [Column(nameof(PaidBy), DataType.Byte)]
        [Required]
        public SpendSource PaidBy { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ContentPurchase> builder)
        {
            _ = builder.OwnEnumeration<ContentPurchase, PurchasableContentType, byte>(t => t.ContentType);
            _ = builder.OwnEnumeration<ContentPurchase, SpendSource, byte>(t => t.PaidBy);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.ContentType, t.ContentId, t.Variant }).IsUnique();
        }
    }
}
