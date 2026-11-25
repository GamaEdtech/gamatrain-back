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
    /// A tutorial, copied from gama-api's <c>dars</c> by <c>ImportTutorials</c> (see docs/business/tutorials.md). The id is
    /// gama-api's own, kept for the tutorials' links. The board, grade, course, subject and topics are gama-api's type ids
    /// (<c>LegacyTaxonomyItems</c>). The owner is this backend's user, matched by <c>CoreId</c> during the copy only.
    /// </summary>
    [Table(nameof(Tutorial))]
    public class Tutorial : IEntity<Tutorial, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>gama-api's owner (<c>user_</c>), kept for the copy's matching only.</summary>
        [Column(nameof(CoreOwnerId), DataType.Long)]
        [Required]
        public long CoreOwnerId { get; set; }

        /// <summary>The owner here, when the gama-api owner has an account linked by <c>CoreId</c>.</summary>
        [Column(nameof(OwnerUserId), DataType.Long)]
        public long? OwnerUserId { get; set; }
        public ApplicationUser? OwnerUser { get; set; }

        /// <summary>The user shown as the author: the owner, or the configured fallback user when the owner has no account here.</summary>
        [Column(nameof(DisplayOwnerUserId), DataType.Long)]
        public long? DisplayOwnerUserId { get; set; }
        public ApplicationUser? DisplayOwnerUser { get; set; }

        /// <summary>gama-api's referee code (<c>referee</c>), history only.</summary>
        [Column(nameof(CoreReferee), DataType.String)]
        [StringLength(10)]
        public string? CoreReferee { get; set; }

        [Column(nameof(CoreBoardId), DataType.Int)]
        public int? CoreBoardId { get; set; }

        [Column(nameof(CoreGradeId), DataType.Int)]
        public int? CoreGradeId { get; set; }

        [Column(nameof(CoreCourseId), DataType.Int)]
        public int? CoreCourseId { get; set; }

        [Column(nameof(CoreSubjectId), DataType.Int)]
        public int? CoreSubjectId { get; set; }

        /// <summary>The topics, as gama-api stores them ("id+id", usually a season and its chapter).</summary>
        [Column(nameof(CoreTopicIds), DataType.String)]
        [StringLength(100)]
        public string? CoreTopicIds { get; set; }

        /// <summary>The order among the subject's tutorials (gama-api's <c>topic_order</c>: its chapter's list order).</summary>
        [Column(nameof(TopicOrder), DataType.Int)]
        public int? TopicOrder { get; set; }

        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(1000)]
        [Required]
        public string? Title { get; set; }

        [Column(nameof(Description), DataType.UnicodeString)]
        [StringLength(1000)]
        public string? Description { get; set; }

        /// <summary>The tutorial's HTML, as gama-api stores it (image links point at gama-api; rewritten and sanitized on output).</summary>
        [Column(nameof(Content), DataType.UnicodeMaxString)]
        public string? Content { get; set; }

        /// <summary>The keywords, joined by " | " (copied from gama-api's content keywords, or generated here by the AI); the
        /// tutorials search matches them as it matches the title.</summary>
        [Column(nameof(Keywords), DataType.UnicodeString)]
        [StringLength(2000)]
        public string? Keywords { get; set; }

        [Column(nameof(ContentSize), DataType.Int)]
        public int? ContentSize { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public TutorialStatus Status { get; set; } = TutorialStatus.Pending;

        [Column(nameof(ViewCount), DataType.Int)]
        [Required]
        public int ViewCount { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        /// <summary>When the copy last wrote this row.</summary>
        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public ICollection<TutorialImage>? Images { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Tutorial> builder)
        {
            _ = builder.OwnEnumeration<Tutorial, TutorialStatus, byte>(t => t.Status);
            _ = builder.HasOne(t => t.OwnerUser).WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.DisplayOwnerUser).WithMany().HasForeignKey(t => t.DisplayOwnerUserId).OnDelete(DeleteBehavior.NoAction);

            // The lists gama-api serves: by subject (with its topics), by board and grade; newest change first.
            _ = builder.HasIndex(t => new { t.CoreSubjectId, t.Status });
            _ = builder.HasIndex(t => new { t.CoreBoardId, t.CoreGradeId, t.Status });
            _ = builder.HasIndex(t => t.OwnerUserId);

            // The admin dashboard's review queue (waiting).
            _ = builder.HasIndex(t => t.CreationDate, "IX_Tutorials_Review_CreationDate").HasFilter("[Status] = 0");
            _ = builder.HasIndex(t => t.LastModifyDate);
        }
    }
}
