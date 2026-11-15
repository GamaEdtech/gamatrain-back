namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A past paper file bought on gama-api (one paid row of its <c>payments</c> table), copied so no buyer pays for it
    /// again after the move. This is a copy of gama-api's history, not a purchase table: ownership is the
    /// <c>ContentPurchases</c> row it becomes (<see cref="ContentPurchaseId"/>, <c>PurchasableContentType.PastPaper</c>),
    /// made as soon as its paper is copied and its buyer has an account here. Only what ownership needs is copied: never
    /// gama-api's card number, IP or bank fields. See docs/business/past-papers.md, "Purchases made on gama-api".
    /// <para>Temporary: it exists only for the move off gama-api and is removed after it (the steps are in that doc,
    /// "Removing the purchase copy"). Nothing outside <c>PastPaperPurchaseImportService</c> may read it; ownership is
    /// always read from <c>ContentPurchases</c>.</para>
    /// </summary>
    [Table(nameof(LegacyPastPaperPurchase))]
    public class LegacyPastPaperPurchase : IEntity<LegacyPastPaperPurchase, long>
    {
        /// <summary>gama-api's payment id, kept as it is: a rerun updates the same row.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>The buyer's gama-api user id (<c>user_</c>), which is <c>ApplicationUser.CoreId</c> here.</summary>
        [Column(nameof(CoreUserId), DataType.Long)]
        [Required]
        public long CoreUserId { get; set; }

        /// <summary>What was bought (<c>usage_id</c>): the paper id, or the extra file's id for <see cref="CoreType"/> 12.</summary>
        [Column(nameof(CoreUsageId), DataType.Long)]
        [Required]
        public long CoreUsageId { get; set; }

        /// <summary>gama-api's payment type: 1 question PDF, 2 answer, 8 question Word, 12 extra file.</summary>
        [Column(nameof(CoreType), DataType.Byte)]
        [Required]
        public byte CoreType { get; set; }

        /// <summary>gama-api's paid status (1 or 2, as its own "already paid" check reads it).</summary>
        [Column(nameof(CoreStatus), DataType.Byte)]
        [Required]
        public byte CoreStatus { get; set; }

        /// <summary>What gama-api charged, in its own unit (Rial); kept for the record only.</summary>
        [Column(nameof(Price), DataType.Long)]
        [Required]
        public long Price { get; set; }

        [Column(nameof(PurchaseDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset PurchaseDate { get; set; }

        /// <summary>The paper the file belongs to, once that paper is copied; null until then.</summary>
        [Column(nameof(PastPaperId), DataType.Long)]
        public long? PastPaperId { get; set; }

        /// <summary>The <c>ContentPurchases</c> variant of the file (Question, Answer, QuestionWord or <c>Extra:{id}</c>).</summary>
        [Column(nameof(Variant), DataType.UnicodeString)]
        [StringLength(50)]
        public string? Variant { get; set; }

        /// <summary>The ownership row this purchase became; null while the paper or the buyer is not here yet.</summary>
        [Column(nameof(ContentPurchaseId), DataType.Long)]
        public long? ContentPurchaseId { get; set; }
        public ContentPurchase? ContentPurchase { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<LegacyPastPaperPurchase> builder)
        {
            _ = builder.HasOne(t => t.ContentPurchase).WithMany().HasForeignKey(t => t.ContentPurchaseId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => t.CoreUserId);
            _ = builder.HasIndex(t => t.ContentPurchaseId);
        }
    }
}
