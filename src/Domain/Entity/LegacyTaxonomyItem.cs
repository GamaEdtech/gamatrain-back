namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// One of gama-api's lookup items (its <c>type_slave</c> row): a board, grade, course, subject, topic, paper type or
    /// classification, copied with its gama-api id so every reference to it keeps working. Read by the native source of
    /// the Types domain; filled by <c>ILegacyTaxonomySyncService</c>. This is a copy of gama-api's model on purpose: it is
    /// redesigned after the migration, not during it (see <c>docs/architecture/legacy-migration.md</c>).
    /// </summary>
    [Table(nameof(LegacyTaxonomyItem))]
    public class LegacyTaxonomyItem : IEntity<LegacyTaxonomyItem, int>
    {
        /// <summary>gama-api's id, kept as it is.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Int)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public int Id { get; set; }

        /// <summary>gama-api's list the item is in (its <c>master_</c>, see <c>LegacyTaxonomyKinds</c>).</summary>
        [Column(nameof(Kind), DataType.Short)]
        [Required]
        public short Kind { get; set; }

        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(500)]
        public string? Title { get; set; }

        [Column(nameof(ListOrder), DataType.Int)]
        [Required]
        public int ListOrder { get; set; }

        /// <summary>gama-api's parent field as it is: parent keys joined by <c>+</c> (a subject's key is <c>course,grade</c>).
        /// Split into <see cref="Parents"/> for lookups.</summary>
        [Column(nameof(Parent), DataType.String)]
        [StringLength(2000)]
        public string? Parent { get; set; }

        [Column(nameof(Parent2), DataType.String)]
        [StringLength(100)]
        public string? Parent2 { get; set; }

        /// <summary>gama-api's metadata JSON, as it sends it (only some lists carry it).</summary>
        [Column(nameof(Metadata), DataType.UnicodeMaxString)]
        public string? Metadata { get; set; }

        /// <summary>A topic that heads a section of the syllabus (gama-api's <c>season</c>).</summary>
        [Column(nameof(IsSeason), DataType.Boolean)]
        public bool? IsSeason { get; set; }

        /// <summary>A classification of past papers (gama-api's <c>is_paper</c>).</summary>
        [Column(nameof(IsPaper), DataType.Boolean)]
        public bool? IsPaper { get; set; }

        /// <summary>No longer listed by gama-api. Kept, not deleted, because other data may still point at it.</summary>
        [Column(nameof(IsDeleted), DataType.Boolean)]
        [Required]
        public bool IsDeleted { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public ICollection<LegacyTaxonomyItemParent>? Parents { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<LegacyTaxonomyItem> builder) =>
            _ = builder.HasIndex(t => new { t.Kind, t.IsDeleted, t.ListOrder });
    }
}
