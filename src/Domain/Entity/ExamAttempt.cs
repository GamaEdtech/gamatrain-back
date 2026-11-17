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
    /// One user's attempt at an exam, copied from gama-api's <c>azmoon_data</c> by <c>ImportExams</c>: made when the exam is
    /// started (<see cref="Status"/> 0) and finished at submit (1, with the score and the time taken). The id is gama-api's own.
    /// </summary>
    [Table(nameof(ExamAttempt))]
    public class ExamAttempt : IEntity<ExamAttempt, long>, IExamCopyRow
    {
        /// <summary>gama-api's <c>azmoon_data.id</c> (the start id of the answers).</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(CoreUserId), DataType.Long)]
        [Required]
        public long CoreUserId { get; set; }

        /// <summary>The user here, linked by <c>CoreId</c>.</summary>
        [Column(nameof(UserId), DataType.Long)]
        public long? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        [Column(nameof(ExamCode), DataType.String)]
        [StringLength(10)]
        public string? ExamCode { get; set; }

        /// <summary>gama-api's <c>submit_time</c>.</summary>
        [Column(nameof(SubmitSeconds), DataType.Int)]
        public int? SubmitSeconds { get; set; }

        /// <summary>gama-api's score percent, as text.</summary>
        [Column(nameof(ResultScore), DataType.String)]
        [StringLength(4)]
        public string? ResultScore { get; set; }

        /// <summary>0 started, 1 finished.</summary>
        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public byte Status { get; set; }

        [Column(nameof(State), DataType.Byte)]
        public byte? State { get; set; }

        [Column(nameof(Area), DataType.Int)]
        public int? Area { get; set; }

        [Column(nameof(School), DataType.Int)]
        public int? School { get; set; }

        [Column(nameof(Transfer), DataType.Boolean)]
        [Required]
        public bool Transfer { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamAttempt> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.ExamId, t.Status });
            _ = builder.HasIndex(t => new { t.UserId, t.CreationDate });
        }
    }
}
