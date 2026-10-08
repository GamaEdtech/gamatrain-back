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
    /// A teacher's past-paper import through the MCP connector (2026-10-08): an AI assistant reads the paper and hands
    /// over the exam details and the extracted questions, which are checked here and then uploaded to gama-api as a
    /// draft exam (see IExamImportService). One per user; working data only, removed once
    /// <c>Mcp:ImportRetentionDays</c> pass without a change.
    /// </summary>
    [Table(nameof(ExamImport))]
    public class ExamImport : IEntity<ExamImport, long>, IUserId<long>, ICreationDate
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

        /// <summary>The exam details as JSON (ExamImportDetailsDto).</summary>
        [Column(nameof(Details), DataType.UnicodeMaxString)]
        public string? Details { get; set; }

        /// <summary>The extracted questions as JSON (ExamImportQuestionDto[]), in paper order.</summary>
        [Column(nameof(Questions), DataType.UnicodeMaxString)]
        public string? Questions { get; set; }

        /// <summary>The upload progress as JSON (ExamImportUploadDto). Its own column, so the background upload and a
        /// tool call writing the questions never overwrite each other.</summary>
        [Column(nameof(Upload), DataType.UnicodeMaxString)]
        public string? Upload { get; set; }

        /// <summary>The owner's gama-api token, encrypted with Data Protection, kept only while the background upload
        /// may need it (there is no request to take it from there).</summary>
        [Column(nameof(GamaToken), DataType.UnicodeMaxString)]
        public string? GamaToken { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(LastModifyDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset LastModifyDate { get; set; }

        public ICollection<ExamImportFigure>? Figures { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamImport> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => t.UserId).IsUnique();
            _ = builder.HasIndex(t => t.LastModifyDate);
        }
    }
}
