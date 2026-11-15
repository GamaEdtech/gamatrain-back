namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A file of a past paper: the question paper, its answer, its Word version or an extra file. <see cref="Path"/> is
    /// the file's key inside the private bucket's <c>PastPaper</c> container, which is gama-api's path inside its paper
    /// folder unchanged (<c>2025/06/22/name.pdf</c>, or <c>name.pdf</c> for older files), so the copied files and rows
    /// match without renaming. The file itself is private: it is only ever reached through a short signed link.
    /// </summary>
    [Table(nameof(PastPaperFile))]
    public class PastPaperFile : IEntity<PastPaperFile, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(PastPaperId), DataType.Long)]
        [Required]
        public long PastPaperId { get; set; }
        public PastPaper? PastPaper { get; set; }

        [Column(nameof(Kind), DataType.Byte)]
        [Required]
        public PastPaperFileKind Kind { get; set; } = PastPaperFileKind.Question;

        [Column(nameof(Path), DataType.String)]
        [StringLength(200)]
        [Required]
        public string? Path { get; set; }

        /// <summary>The file type gama-api recorded (<c>q_file_ext</c>, <c>a_file_ext</c>); otherwise from the path.</summary>
        [Column(nameof(Extension), DataType.String)]
        [StringLength(10)]
        public string? Extension { get; set; }

        [Column(nameof(PageCount), DataType.Short)]
        public short? PageCount { get; set; }

        /// <summary>An extra file's length (<c>tests_extra_files.length</c>), e.g. an audio file's duration.</summary>
        [Column(nameof(Length), DataType.Int)]
        public int? Length { get; set; }

        /// <summary>What an extra file is (<c>tests_extra_files.type</c>), a gama-api lookup item id.</summary>
        [Column(nameof(CoreTypeId), DataType.Int)]
        public int? CoreTypeId { get; set; }

        /// <summary>An extra file's gama-api id (<c>tests_extra_files.id</c>); null for the paper's own files.</summary>
        [Column(nameof(CoreExtraFileId), DataType.Long)]
        public long? CoreExtraFileId { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? CreationDate { get; set; }

        /// <summary>The stored file's size in bytes, from the private bucket; null until checked, or when the file is missing.</summary>
        [Column(nameof(Size), DataType.Long)]
        public long? Size { get; set; }

        /// <summary>When <c>CheckPastPaperFiles</c> last looked for the file. Checked with no <see cref="Size"/> means missing.</summary>
        [Column(nameof(CheckDate), DataType.DateTimeOffset)]
        public DateTimeOffset? CheckDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PastPaperFile> builder)
        {
            _ = builder.OwnEnumeration<PastPaperFile, PastPaperFileKind, byte>(t => t.Kind);
            _ = builder.HasOne(t => t.PastPaper).WithMany(t => t.Files).HasForeignKey(t => t.PastPaperId).OnDelete(DeleteBehavior.Cascade);

            // One question, answer and Word file per paper; any number of extras, each copied once.
            _ = builder.HasIndex(t => new { t.PastPaperId, t.Kind }).IsUnique().HasFilter("[Kind] <> 4");
            _ = builder.HasIndex(t => t.CoreExtraFileId).IsUnique().HasFilter("[CoreExtraFileId] IS NOT NULL");
        }
    }
}
