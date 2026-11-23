namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// An exam review reminder sent (2026-11-23): one row per user and exam, written before the email goes, so a user is
    /// reminded of an exam at most once ever (the unique index), and of exams at most once a day (<see cref="SentDate"/>).
    /// See docs/business/notifications.md, "Exam review reminder".
    /// </summary>
    [Table(nameof(ExamReviewReminder))]
    public class ExamReviewReminder : IEntity<ExamReviewReminder, long>
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

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        [Column(nameof(SentDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SentDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamReviewReminder> builder)
        {
            _ = builder.HasIndex(t => new { t.UserId, t.ExamId }).IsUnique();
            _ = builder.HasIndex(t => new { t.UserId, t.SentDate });
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
