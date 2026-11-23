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
    /// A participant's review of an exam (2026-11-23): a 1-5 rating and an optional comment, one per user and exam, by someone
    /// who finished it. Public at once (<see cref="Status.Confirmed"/>), like a school's comments; an admin hides a bad one
    /// (<see cref="Status.Rejected"/>). The exam page shows the confirmed ones. See docs/business/exams-and-content.md,
    /// "Exam reviews".
    /// </summary>
    [Table(nameof(ExamReview))]
    public class ExamReview : IEntity<ExamReview, long>, IUserId<long>
    {
        public const int MaxCommentLength = 1000;

        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }
        public Exam? Exam { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>1 to 5.</summary>
        [Column(nameof(Rating), DataType.Byte)]
        [Required]
        public byte Rating { get; set; }

        /// <summary>Plain text (the page prints it as text), at most <see cref="MaxCommentLength"/> characters.</summary>
        [Column(nameof(Comment), DataType.UnicodeString)]
        [StringLength(MaxCommentLength)]
        public string? Comment { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public Status Status { get; set; } = Status.Confirmed;

        /// <summary>Why an admin hid it, for its author.</summary>
        [Column(nameof(RejectionComment), DataType.UnicodeString)]
        [StringLength(300)]
        public string? RejectionComment { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamReview> builder)
        {
            _ = builder.OwnEnumeration<ExamReview, Status, byte>(t => t.Status);
            _ = builder.HasIndex(t => new { t.ExamId, t.UserId }).IsUnique();
            _ = builder.HasIndex(t => new { t.ExamId, t.Status });
            _ = builder.HasOne(t => t.Exam).WithMany().HasForeignKey(t => t.ExamId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
