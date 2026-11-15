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
    /// A past paper. Its <see cref="Id"/> is the paper's gama-api id, kept as it is, so every existing link, purchase and
    /// download record still points at the same paper. Until the past paper domain is switched to this table (a separate
    /// change), it is only a copy filled by <c>IPastPaperImportService</c> and nothing reads it; gama-api stays the source.
    /// The <c>Core...</c> columns are gama-api ids (users, teachers, lookup items) with no foreign key here yet.
    /// See <c>docs/business/past-papers.md</c>.
    /// </summary>
    [Table(nameof(PastPaper))]
    public class PastPaper : IEntity<PastPaper, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>The owner's gama-api user id (<c>user_</c>), which is <c>ApplicationUser.CoreId</c> here.</summary>
        [Column(nameof(CoreOwnerId), DataType.Long)]
        [Required]
        public long CoreOwnerId { get; set; }

        /// <summary>This backend's user with that <c>CoreId</c>; null until that user is linked.</summary>
        [Column(nameof(OwnerUserId), DataType.Long)]
        public long? OwnerUserId { get; set; }
        public ApplicationUser? OwnerUser { get; set; }

        /// <summary>The owner's gama-api teacher profile id (<c>teacher_</c>).</summary>
        [Column(nameof(CoreTeacherId), DataType.Long)]
        public long? CoreTeacherId { get; set; }

        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(500)]
        [Required]
        public string? Title { get; set; }

        [Column(nameof(Description), DataType.UnicodeMaxString)]
        public string? Description { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public PastPaperStatus Status { get; set; } = PastPaperStatus.Pending;

        /// <summary>The reviewer's note to the owner (<c>msg</c>).</summary>
        [Column(nameof(ReviewNote), DataType.UnicodeMaxString)]
        public string? ReviewNote { get; set; }

        /// <summary>gama-api's <c>bit_delete</c>: removed for everyone.</summary>
        [Column(nameof(IsDeleted), DataType.Boolean)]
        [Required]
        public bool IsDeleted { get; set; }

        /// <summary>Board (<c>section</c>), a gama-api lookup item id.</summary>
        [Column(nameof(CoreBoardId), DataType.Int)]
        [Required]
        public int CoreBoardId { get; set; }

        /// <summary>Grade (<c>base</c>).</summary>
        [Column(nameof(CoreGradeId), DataType.Int)]
        [Required]
        public int CoreGradeId { get; set; }

        /// <summary>Subject (<c>lesson</c>).</summary>
        [Column(nameof(CoreSubjectId), DataType.Int)]
        [Required]
        public int CoreSubjectId { get; set; }

        /// <summary>Course (<c>course</c>); empty in gama-api today.</summary>
        [Column(nameof(CoreCourseId), DataType.Int)]
        public int? CoreCourseId { get; set; }

        /// <summary>Topics (<c>topic</c>), gama-api's comma-separated lookup item ids, kept as written.</summary>
        [Column(nameof(CoreTopicIds), DataType.String)]
        [StringLength(200)]
        public string? CoreTopicIds { get; set; }

        /// <summary>Paper type / paper number (<c>test_type</c>).</summary>
        [Column(nameof(CorePaperTypeId), DataType.Int)]
        public int? CorePaperTypeId { get; set; }

        [Column(nameof(Variant), DataType.Short)]
        public short? Variant { get; set; }

        /// <summary>The board's subject code, e.g. 9702.</summary>
        [Column(nameof(Code), DataType.String)]
        [StringLength(10)]
        public string? Code { get; set; }

        /// <summary>1 easy, 2 medium, 3 hard, 4 very hard.</summary>
        [Column(nameof(Level), DataType.Byte)]
        public byte? Level { get; set; }

        [Column(nameof(AnswerType), DataType.Byte)]
        public byte? AnswerType { get; set; }

        [Column(nameof(HoldingLevel), DataType.Short)]
        public short? HoldingLevel { get; set; }

        [Column(nameof(State), DataType.UnicodeString)]
        [StringLength(50)]
        public string? State { get; set; }

        [Column(nameof(CoreAreaId), DataType.Int)]
        public int? CoreAreaId { get; set; }

        [Column(nameof(CoreSchoolId), DataType.Long)]
        public long? CoreSchoolId { get; set; }

        /// <summary>The exam session the paper is from: year, month and session.</summary>
        [Column(nameof(EduYear), DataType.Short)]
        public short? EduYear { get; set; }

        [Column(nameof(EduMonth), DataType.Byte)]
        public byte? EduMonth { get; set; }

        [Column(nameof(EduSession), DataType.Byte)]
        public byte? EduSession { get; set; }

        /// <summary>gama-api exam ids built from this paper (<c>exam_id</c>), as written.</summary>
        [Column(nameof(CoreExamIds), DataType.String)]
        [StringLength(300)]
        public string? CoreExamIds { get; set; }

        [Column(nameof(IsPaper), DataType.Boolean)]
        [Required]
        public bool IsPaper { get; set; }

        /// <summary>The owner agreed to give the paper away free (<c>free_aggrement</c>).</summary>
        [Column(nameof(FreeAgreement), DataType.Boolean)]
        [Required]
        public bool FreeAgreement { get; set; }

        /// <summary>Multiplies the paper's price; 1 by default.</summary>
        [Column(nameof(PriceMultiplier), DataType.Decimal)]
        [Required]
        public decimal PriceMultiplier { get; set; } = 1;

        /// <summary>The reviewer's quality score (<c>ref_score</c>).</summary>
        [Column(nameof(RefereeScore), DataType.Byte)]
        [Required]
        public byte RefereeScore { get; set; }

        /// <summary>The reviewer (<c>referee</c>), as gama-api wrote it.</summary>
        [Column(nameof(CoreReferee), DataType.String)]
        [StringLength(10)]
        public string? CoreReferee { get; set; }

        [Column(nameof(Score), DataType.Int)]
        [Required]
        public int Score { get; set; }

        /// <summary>The score given to the teacher (<c>teacher_score</c>).</summary>
        [Column(nameof(TeacherScore), DataType.Byte)]
        [Required]
        public byte TeacherScore { get; set; }

        /// <summary>The thumbnail image (<c>thumb_pic</c>), a public file.</summary>
        [Column(nameof(Thumbnail), DataType.String)]
        [StringLength(100)]
        public string? Thumbnail { get; set; }

        [Column(nameof(ViewCount), DataType.Int)]
        [Required]
        public int ViewCount { get; set; }

        [Column(nameof(DownloadCount), DataType.Int)]
        [Required]
        public int DownloadCount { get; set; }

        [Column(nameof(PaidCount), DataType.Int)]
        [Required]
        public int PaidCount { get; set; }

        /// <summary>When the paper was submitted (<c>subdate</c>).</summary>
        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        /// <summary>The paper's last edit (<c>up_date</c>).</summary>
        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastModifyDate { get; set; }

        /// <summary>When the copy last read this paper from gama-api.</summary>
        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public ICollection<PastPaperFile>? Files { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PastPaper> builder)
        {
            _ = builder.OwnEnumeration<PastPaper, PastPaperStatus, byte>(t => t.Status);
            _ = builder.Property(t => t.PriceMultiplier).HasPrecision(9, 4);
            _ = builder.HasOne(t => t.OwnerUser).WithMany().HasForeignKey(t => t.OwnerUserId).OnDelete(DeleteBehavior.NoAction);

            // The lists gama-api serves today: by subject, by board and grade, by owner; the default sort is newest first.
            _ = builder.HasIndex(t => new { t.CoreSubjectId, t.Status, t.IsDeleted });
            _ = builder.HasIndex(t => new { t.CoreBoardId, t.CoreGradeId, t.Status, t.IsDeleted });
            _ = builder.HasIndex(t => new { t.OwnerUserId, t.Status });
            _ = builder.HasIndex(t => t.CoreOwnerId);
            _ = builder.HasIndex(t => t.CreationDate);
        }
    }
}
