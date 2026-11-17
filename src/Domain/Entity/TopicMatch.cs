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
    /// A suggested match between a GAMA topic and a topic of another board (gama-api's <c>topic_found_matches</c>, copied by
    /// <c>ImportTopicMatches</c>, or suggested here). Approving it links the two topics (<c>LegacyTaxonomyRelations</c>), which
    /// makes each board's lists show the other's content. The ids are gama-api's lookup ids. See docs/business/tutorials.md,
    /// "Topic matching".
    /// </summary>
    [Table(nameof(TopicMatch))]
    public class TopicMatch : IEntity<TopicMatch, long>
    {
        /// <summary>gama-api's id for a copied match; new ones continue above the highest.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(GamaTopicId), DataType.Int)]
        [Required]
        public int GamaTopicId { get; set; }

        /// <summary>The GAMA topic's subject.</summary>
        [Column(nameof(GamaSubjectId), DataType.Int)]
        [Required]
        public int GamaSubjectId { get; set; }

        [Column(nameof(ExternalTopicId), DataType.Int)]
        [Required]
        public int ExternalTopicId { get; set; }

        /// <summary>The other board's topic's subject.</summary>
        [Column(nameof(ExternalSubjectId), DataType.Int)]
        [Required]
        public int ExternalSubjectId { get; set; }

        /// <summary>How sure the AI was, 0-100 (a manual match keeps what was given).</summary>
        [Column(nameof(Confidence), DataType.Byte)]
        [Required]
        public byte Confidence { get; set; }

        [Column(nameof(Reasoning), DataType.UnicodeString)]
        [StringLength(2000)]
        public string? Reasoning { get; set; }

        /// <summary>The AI model that suggested it, or <c>manual</c>.</summary>
        [Column(nameof(AiModel), DataType.String)]
        [StringLength(50)]
        public string? AiModel { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public TopicMatchStatus Status { get; set; } = TopicMatchStatus.Pending;

        /// <summary>Who approved it here.</summary>
        [Column(nameof(ReviewedByUserId), DataType.Long)]
        public long? ReviewedByUserId { get; set; }
        public ApplicationUser? ReviewedByUser { get; set; }

        /// <summary>gama-api's reviewer (<c>reviewed_by</c>), history only.</summary>
        [Column(nameof(CoreReviewedBy), DataType.Long)]
        public long? CoreReviewedBy { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        /// <summary>When the copy or an admin last wrote this row.</summary>
        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<TopicMatch> builder)
        {
            _ = builder.OwnEnumeration<TopicMatch, TopicMatchStatus, byte>(t => t.Status);
            _ = builder.HasOne(t => t.ReviewedByUser).WithMany().HasForeignKey(t => t.ReviewedByUserId).OnDelete(DeleteBehavior.NoAction);

            // One suggestion per pair, as gama-api's unique key; the review lists go by status and by GAMA subject.
            _ = builder.HasIndex(t => new { t.GamaTopicId, t.ExternalTopicId }).IsUnique();
            _ = builder.HasIndex(t => t.ExternalTopicId);
            _ = builder.HasIndex(t => new { t.GamaSubjectId, t.Status });
            _ = builder.HasIndex(t => t.Status);
        }
    }
}
