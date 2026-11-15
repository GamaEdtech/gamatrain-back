namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A finished online exam, one row per user and exam: written by the exam result proxy (<c>GET exams/{id}/result</c>) the
    /// first time the user opens the result, together with the exam's points (<c>IGameService.RecordExamSubmissionAsync</c>).
    /// A retake keeps the first result, as the points do. See docs/business/exams-and-content.md.
    /// </summary>
    [Table(nameof(ExamSubmission))]
    public class ExamSubmission : IEntity<ExamSubmission, long>, ICreationDate, IUserId<long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser User { get; set; }

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(Valid), DataType.Int)]
        [Required]
        public int Valid { get; set; }

        [Column(nameof(Invalid), DataType.Int)]
        [Required]
        public int Invalid { get; set; }

        [Column(nameof(NoAnswer), DataType.Int)]
        [Required]
        public int NoAnswer { get; set; }

        /// <summary>gama-api's score percent (it may apply negative marking); null for rows recorded before 2026-11-15.</summary>
        [Column(nameof(Percent), DataType.Decimal)]
        public decimal? Percent { get; set; }

        [Column(nameof(ExamTitle), DataType.UnicodeString)]
        [StringLength(500)]
        public string? ExamTitle { get; set; }

        [Column(nameof(TimeTakenSeconds), DataType.Int)]
        public int? TimeTakenSeconds { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamSubmission> builder)
        {
            _ = builder.HasIndex(t => new { t.UserId, t.ExamId }).IsUnique();
            _ = builder.HasIndex(t => new { t.UserId, t.CreationDate });
            _ = builder.Property(t => t.Percent).HasPrecision(6, 2);
        }
    }
}
