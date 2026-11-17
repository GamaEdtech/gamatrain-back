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
    /// A finished attempt's answers, copied from gama-api's <c>azmoon_results</c> by <c>ImportExams</c>: the chosen
    /// answer per question, as gama-api stores them ("question:choice,..."). The id is gama-api's own.
    /// </summary>
    [Table(nameof(ExamAnswerSheet))]
    public class ExamAnswerSheet : IEntity<ExamAnswerSheet, long>, IExamCopyRow
    {
        /// <summary>gama-api's <c>azmoon_results.id</c>.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(CoreUserId), DataType.Long)]
        [Required]
        public long CoreUserId { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        public long? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        /// <summary>The attempt (gama-api's <c>start_id</c>).</summary>
        [Column(nameof(AttemptId), DataType.Long)]
        public long? AttemptId { get; set; }

        /// <summary>gama-api's <c>tests</c>: "question:choice" pairs.</summary>
        [Column(nameof(Answers), DataType.String)]
        [StringLength(4000)]
        public string? Answers { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamAnswerSheet> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => t.AttemptId);
            _ = builder.HasIndex(t => new { t.UserId, t.ExamId });
        }
    }
}
