namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// An image (diagram, graph, table...) the AI cut from the paper for an <see cref="ExamImport"/> question. Kept in the
    /// database, not the file provider: it is short-lived working data that the background upload must read back with no
    /// request around (the local file provider builds its URLs from the request), and it goes with its import.
    /// </summary>
    [Table(nameof(ExamImportFigure))]
    public class ExamImportFigure : IEntity<ExamImportFigure, long>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(ExamImportId), DataType.Long)]
        [Required]
        public long ExamImportId { get; set; }
        public ExamImport? ExamImport { get; set; }

        [Column(nameof(Name), DataType.UnicodeString)]
        [StringLength(200)]
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary><c>image/png</c> or <c>image/jpeg</c>, the only image types gama-api takes for a question.</summary>
        [Column(nameof(ContentType), DataType.String)]
        [StringLength(50)]
        [Required]
        public string ContentType { get; set; } = string.Empty;

#pragma warning disable CA1819 // Properties should not return arrays
        [Column(nameof(Content), DataType.File)]
        [Required]
        public byte[] Content { get; set; } = [];
#pragma warning restore CA1819 // Properties should not return arrays

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamImportFigure> builder) =>
            _ = builder.HasOne(t => t.ExamImport).WithMany(t => t.Figures).HasForeignKey(t => t.ExamImportId).OnDelete(DeleteBehavior.Cascade);
    }
}
