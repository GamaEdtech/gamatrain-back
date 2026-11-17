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
    /// An exam, copied from gama-api's <c>azmoons</c> by <c>ImportExams</c> (see docs/business/exams-migration.md). The id is
    /// gama-api's own, kept for the exams' links; new exams will continue above it. The board, grade, course, subject, topics and
    /// exam type are gama-api's type ids (<c>LegacyTaxonomyItems</c>). The questions are <see cref="ExamQuestion"/> rows, in
    /// the order of <see cref="QuestionIds"/>.
    /// </summary>
    [Table(nameof(Exam))]
    public class Exam : IEntity<Exam, long>, IExamCopyStampedRow
    {
        /// <summary>gama-api's <c>azmoons.id</c>.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>The exam this one was cloned from.</summary>
        [Column(nameof(CloneId), DataType.Long)]
        public long? CloneId { get; set; }

        /// <summary>gama-api's owner (<c>user_</c>), kept for the copy's matching.</summary>
        [Column(nameof(CoreOwnerId), DataType.Long)]
        [Required]
        public long CoreOwnerId { get; set; }

        /// <summary>The owner here, when the gama-api owner has an account linked by <c>CoreId</c>.</summary>
        [Column(nameof(OwnerUserId), DataType.Long)]
        public long? OwnerUserId { get; set; }
        public ApplicationUser? OwnerUser { get; set; }

        [Column(nameof(CoreReferee), DataType.String)]
        [StringLength(10)]
        public string? CoreReferee { get; set; }

        /// <summary>gama-api's <c>user_type</c>: the owner's kind when it was made.</summary>
        [Column(nameof(UserType), DataType.Byte)]
        public byte? UserType { get; set; }

        [Column(nameof(CoreBoardId), DataType.Int)]
        public int? CoreBoardId { get; set; }

        [Column(nameof(CoreGradeId), DataType.Int)]
        public int? CoreGradeId { get; set; }

        [Column(nameof(CoreCourseId), DataType.Int)]
        public int? CoreCourseId { get; set; }

        [Column(nameof(CoreSubjectId), DataType.Int)]
        public int? CoreSubjectId { get; set; }

        /// <summary>gama-api's topics ("id+id").</summary>
        [Column(nameof(CoreTopicIds), DataType.String)]
        [StringLength(200)]
        public string? CoreTopicIds { get; set; }

        /// <summary>gama-api's <c>type</c> (1 an online exam; other codes are older kinds).</summary>
        [Column(nameof(Type), DataType.Byte)]
        [Required]
        public byte Type { get; set; }

        /// <summary>gama-api's exam type (<c>azmoon_type</c>, a type id).</summary>
        [Column(nameof(CoreExamTypeId), DataType.Int)]
        public int? CoreExamTypeId { get; set; }

        [Column(nameof(HoldingLevel), DataType.Byte)]
        public byte? HoldingLevel { get; set; }

        [Column(nameof(State), DataType.UnicodeString)]
        [StringLength(50)]
        public string? State { get; set; }

        [Column(nameof(Area), DataType.Int)]
        public int? Area { get; set; }

        [Column(nameof(School), DataType.Int)]
        public int? School { get; set; }

        [Column(nameof(Level), DataType.Byte)]
        public byte? Level { get; set; }

        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(500)]
        [Required]
        public string? Title { get; set; }

        /// <summary>The questions in order, gama-api's comma-separated <c>tests</c>.</summary>
        [Column(nameof(QuestionIds), DataType.String)]
        [StringLength(5000)]
        public string? QuestionIds { get; set; }

        [Column(nameof(QuestionCount), DataType.Int)]
        [Required]
        public int QuestionCount { get; set; }

        [Column(nameof(QuestionsCombination), DataType.Byte)]
        public byte? QuestionsCombination { get; set; }

        [Column(nameof(StartDate), DataType.DateTimeOffset)]
        public DateTimeOffset? StartDate { get; set; }

        [Column(nameof(EndDate), DataType.DateTimeOffset)]
        public DateTimeOffset? EndDate { get; set; }

        /// <summary>gama-api's <c>azmoon_time</c>.</summary>
        [Column(nameof(DurationMinutes), DataType.Int)]
        public int? DurationMinutes { get; set; }

        [Column(nameof(ScoreType), DataType.Byte)]
        public byte? ScoreType { get; set; }

        /// <summary>gama-api's short exam code.</summary>
        [Column(nameof(Code), DataType.String)]
        [StringLength(10)]
        public string? Code { get; set; }

        [Column(nameof(EduYear), DataType.Int)]
        public int? EduYear { get; set; }

        [Column(nameof(EduMonth), DataType.Byte)]
        public byte? EduMonth { get; set; }

        /// <summary>gama-api's original file name, history only: files are not copied (the export generates an exam's files).</summary>
        [Column(nameof(FileOriginal), DataType.String)]
        [StringLength(100)]
        public string? FileOriginal { get; set; }

        /// <summary>gama-api's PDF name, history only: the export generates the PDF on request.</summary>
        [Column(nameof(FilePdf), DataType.String)]
        [StringLength(100)]
        public string? FilePdf { get; set; }

        [Column(nameof(PageBreakPoints), DataType.String)]
        [StringLength(50)]
        public string? PageBreakPoints { get; set; }

        /// <summary>gama-api's thumbnail path (<c>uploads/thumbs/...</c>).</summary>
        [Column(nameof(Thumbnail), DataType.String)]
        [StringLength(200)]
        public string? Thumbnail { get; set; }

        [Column(nameof(ParticipantCount), DataType.Int)]
        [Required]
        public int ParticipantCount { get; set; }

        /// <summary>gama-api's <c>status</c> (1 published).</summary>
        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public byte Status { get; set; }

        /// <summary>gama-api's <c>msg</c>.</summary>
        [Column(nameof(Message), DataType.UnicodeString)]
        [StringLength(100)]
        public string? Message { get; set; }

        [Column(nameof(Review), DataType.Byte)]
        public byte? Review { get; set; }

        /// <summary>gama-api's JSON <c>metadata</c> (e.g. the paper it was built from).</summary>
        [Column(nameof(Metadata), DataType.UnicodeString)]
        [StringLength(1000)]
        public string? Metadata { get; set; }

        [Column(nameof(ViewCount), DataType.Int)]
        [Required]
        public int ViewCount { get; set; }

        [Column(nameof(DownloadCount), DataType.Int)]
        [Required]
        public int DownloadCount { get; set; }

        /// <summary>gama-api's <c>bit_delete</c>.</summary>
        [Column(nameof(IsDeleted), DataType.Boolean)]
        [Required]
        public bool IsDeleted { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        /// <summary>When the copy last wrote this row.</summary>
        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Exam> builder)
        {
            _ = builder.HasOne(t => t.OwnerUser).WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.CoreSubjectId, t.Status });
            _ = builder.HasIndex(t => new { t.CoreBoardId, t.CoreGradeId, t.Status });
            _ = builder.HasIndex(t => t.OwnerUserId);
            _ = builder.HasIndex(t => t.Code);
            _ = builder.HasIndex(t => t.LastModifyDate);
        }
    }
}
