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
    /// A file change the owner made to a published paper, waiting for review (since 2026-11-25). The published paper and its
    /// <see cref="PastPaperFile"/> rows stay as they are, and stay on the website, until an admin approves the edit; then each
    /// row is applied to the paper's files and removed. A rejected edit removes them. Nothing that serves papers reads this
    /// table. See <c>docs/business/past-papers.md</c>, "Edits to a published paper".
    /// </summary>
    [Table(nameof(PastPaperPendingFile))]
    public class PastPaperPendingFile : IEntity<PastPaperPendingFile, long>
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

        /// <summary>For an extra file: the published extra (<see cref="PastPaperFile.CoreExtraFileId"/>) this changes or removes;
        /// null for a new extra file.</summary>
        [Column(nameof(CoreExtraFileId), DataType.Long)]
        public long? CoreExtraFileId { get; set; }

        /// <summary>The file is removed; the other file columns are empty.</summary>
        [Column(nameof(IsRemoval), DataType.Boolean)]
        [Required]
        public bool IsRemoval { get; set; }

        /// <summary>The new file's key in the private bucket's <c>PastPaper</c> container; null for a removal, or for an extra
        /// file whose type alone changes.</summary>
        [Column(nameof(Path), DataType.String)]
        [StringLength(200)]
        public string? Path { get; set; }

        [Column(nameof(Extension), DataType.String)]
        [StringLength(10)]
        public string? Extension { get; set; }

        [Column(nameof(Size), DataType.Long)]
        public long? Size { get; set; }

        [Column(nameof(PageCount), DataType.Short)]
        public short? PageCount { get; set; }

        /// <summary>An extra file's type, a gama-api lookup item id.</summary>
        [Column(nameof(CoreTypeId), DataType.Int)]
        public int? CoreTypeId { get; set; }

        /// <summary>A new question PDF's first page, the paper's thumbnail once approved.</summary>
        [Column(nameof(Thumbnail), DataType.String)]
        [StringLength(100)]
        public string? Thumbnail { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PastPaperPendingFile> builder)
        {
            _ = builder.OwnEnumeration<PastPaperPendingFile, PastPaperFileKind, byte>(t => t.Kind);
            _ = builder.HasOne(t => t.PastPaper).WithMany(t => t.PendingFiles).HasForeignKey(t => t.PastPaperId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasIndex(t => t.PastPaperId);
        }
    }
}
