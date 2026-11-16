namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A link between two lookup items of different boards (gama-api's <c>types_relation</c>, mirrored with its ids by the
    /// types copy). A paper list filtered by a board, grade, subject or topic also shows the papers of the topics linked to
    /// it, so the native paper list needs these links. A link works both ways.
    /// </summary>
    [Table(nameof(LegacyTaxonomyRelation))]
    public class LegacyTaxonomyRelation : IEntity<LegacyTaxonomyRelation, int>
    {
        /// <summary>gama-api's <c>types_relation.id</c>, kept as it is.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Int)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public int Id { get; set; }

        /// <summary>A lookup item id (<c>from_id</c>).</summary>
        [Column(nameof(FromId), DataType.Int)]
        [Required]
        public int FromId { get; set; }

        /// <summary>The lookup item it is linked to (<c>to_id</c>).</summary>
        [Column(nameof(ToId), DataType.Int)]
        [Required]
        public int ToId { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<LegacyTaxonomyRelation> builder)
        {
        }
    }
}
