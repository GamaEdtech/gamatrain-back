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
    /// A question of the exams' bank, copied from gama-api's <c>azmoon_tests</c> by <c>ImportExams</c>. The id is gama-api's
    /// own. The text and answers are HTML as gama-api stores them; their images are in the public <c>ExamImage</c> container.
    /// </summary>
    [Table(nameof(ExamQuestion))]
    public class ExamQuestion : IEntity<ExamQuestion, long>, IExamCopyStampedRow
    {
        /// <summary>gama-api's <c>azmoon_tests.id</c>.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>gama-api's owner (<c>user_</c>), kept for the copy's matching.</summary>
        [Column(nameof(CoreOwnerId), DataType.Long)]
        [Required]
        public long CoreOwnerId { get; set; }

        /// <summary>The owner here, when the gama-api owner has an account linked by <c>CoreId</c>.</summary>
        [Column(nameof(OwnerUserId), DataType.Long)]
        public long? OwnerUserId { get; set; }
        public ApplicationUser? OwnerUser { get; set; }

        /// <summary>
        /// gama-api's id of a row copied after exams were switched to this backend (the add-only copy), which got a new id
        /// here; null for every other row (a row copied before the switch keeps gama-api's id as its own, one made here has none).
        /// Unique, so the copy never brings the same row twice.
        /// </summary>
        [Column(nameof(LegacyId), DataType.Long)]
        public long? LegacyId { get; set; }

        [Column(nameof(Code), DataType.String)]
        [StringLength(10)]
        public string? Code { get; set; }

        [Column(nameof(CodeOrder), DataType.String)]
        [StringLength(10)]
        public string? CodeOrder { get; set; }

        [Column(nameof(Parent), DataType.String)]
        [StringLength(10)]
        public string? Parent { get; set; }

        [Column(nameof(CoreBoardId), DataType.Int)]
        public int? CoreBoardId { get; set; }

        [Column(nameof(CoreGradeId), DataType.Int)]
        public int? CoreGradeId { get; set; }

        [Column(nameof(CoreCourseId), DataType.Int)]
        public int? CoreCourseId { get; set; }

        [Column(nameof(CoreSubjectId), DataType.Int)]
        public int? CoreSubjectId { get; set; }

        [Column(nameof(Direction), DataType.String)]
        [StringLength(5)]
        public string? Direction { get; set; }

        [Column(nameof(CoreTopicIds), DataType.String)]
        [StringLength(100)]
        public string? CoreTopicIds { get; set; }

        [Column(nameof(Level), DataType.Byte)]
        public byte? Level { get; set; }

        [Column(nameof(Type), DataType.Byte)]
        public byte? Type { get; set; }

        [Column(nameof(EnhancedTitle), DataType.UnicodeString)]
        [StringLength(500)]
        public string? EnhancedTitle { get; set; }

        [Column(nameof(Question), DataType.UnicodeMaxString)]
        public string? Question { get; set; }

        [Column(nameof(QuestionFile), DataType.String)]
        [StringLength(100)]
        public string? QuestionFile { get; set; }

        [Column(nameof(AnswerA), DataType.UnicodeMaxString)]
        public string? AnswerA { get; set; }

        [Column(nameof(AnswerB), DataType.UnicodeMaxString)]
        public string? AnswerB { get; set; }

        [Column(nameof(AnswerC), DataType.UnicodeMaxString)]
        public string? AnswerC { get; set; }

        [Column(nameof(AnswerD), DataType.UnicodeMaxString)]
        public string? AnswerD { get; set; }

        /// <summary>The worked answer (gama-api's <c>answer_full</c>).</summary>
        [Column(nameof(AnswerFull), DataType.UnicodeMaxString)]
        public string? AnswerFull { get; set; }

        [Column(nameof(AnswerViewType), DataType.Byte)]
        public byte? AnswerViewType { get; set; }

        [Column(nameof(FileA), DataType.String)]
        [StringLength(200)]
        public string? FileA { get; set; }

        [Column(nameof(FileB), DataType.String)]
        [StringLength(200)]
        public string? FileB { get; set; }

        [Column(nameof(FileC), DataType.String)]
        [StringLength(200)]
        public string? FileC { get; set; }

        [Column(nameof(FileD), DataType.String)]
        [StringLength(200)]
        public string? FileD { get; set; }

        [Column(nameof(AnswerFullFile), DataType.String)]
        [StringLength(200)]
        public string? AnswerFullFile { get; set; }

        /// <summary>The correct choice, 1 to 4.</summary>
        [Column(nameof(TrueAnswer), DataType.Byte)]
        public byte? TrueAnswer { get; set; }

        [Column(nameof(Resource), DataType.UnicodeString)]
        [StringLength(200)]
        public string? Resource { get; set; }

        [Column(nameof(UsedConfirm), DataType.Int)]
        [Required]
        public int UsedConfirm { get; set; }

        [Column(nameof(UsedPublish), DataType.Int)]
        [Required]
        public int UsedPublish { get; set; }

        [Column(nameof(AnswersStat), DataType.String)]
        [StringLength(50)]
        public string? AnswersStat { get; set; }

        /// <summary>gama-api's <c>status</c> (0 waiting, 1 confirmed, 2 rejected).</summary>
        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public byte Status { get; set; }

        [Column(nameof(Message), DataType.UnicodeString)]
        [StringLength(100)]
        public string? Message { get; set; }

        [Column(nameof(TeacherScore), DataType.Byte)]
        public byte? TeacherScore { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        /// <summary>When the copy last wrote this row.</summary>
        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamQuestion> builder)
        {
            _ = builder.HasOne(t => t.OwnerUser).WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.CoreSubjectId, t.Status });
            _ = builder.HasIndex(t => t.OwnerUserId);

            // The admin dashboard's review queue (waiting).
            _ = builder.HasIndex(t => t.CreationDate, "IX_ExamQuestions_Review_CreationDate").HasFilter("[Status] = 0");
            _ = builder.HasIndex(t => t.LastModifyDate);
            _ = builder.HasIndex(t => t.LegacyId).IsUnique().HasFilter($"[{nameof(LegacyId)}] IS NOT NULL");
        }
    }
}
