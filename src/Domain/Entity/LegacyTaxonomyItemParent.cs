namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// One parent key of a <see cref="LegacyTaxonomyItem"/>, so "the items under X" is an indexed lookup instead of
    /// gama-api's <c>LIKE '%+X+%'</c> on the joined field.
    /// </summary>
    [Table(nameof(LegacyTaxonomyItemParent))]
    public class LegacyTaxonomyItemParent : IEntity<LegacyTaxonomyItemParent, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(ItemId), DataType.Int)]
        [Required]
        public int ItemId { get; set; }

        public LegacyTaxonomyItem? Item { get; set; }

        /// <summary>A parent's id, or <c>course,grade</c> for a subject.</summary>
        [Column(nameof(ParentKey), DataType.String)]
        [StringLength(50)]
        [Required]
        public string? ParentKey { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<LegacyTaxonomyItemParent> builder)
        {
            _ = builder.HasIndex(t => new { t.ParentKey, t.ItemId }).IsUnique(true);
            _ = builder.HasOne(t => t.Item).WithMany(t => t.Parents).HasForeignKey(t => t.ItemId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        }
    }
}
