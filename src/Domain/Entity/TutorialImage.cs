namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>An image uploaded for a tutorial's content (gama-api's <c>tutorials_files</c>), stored in the public bucket's
    /// <c>Tutorial</c> container at gama-api's own path (<c>YYYY/MM/{tutorial}_{name}.ext</c>).</summary>
    [Table(nameof(TutorialImage))]
    public class TutorialImage : IEntity<TutorialImage, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(TutorialId), DataType.Long)]
        [Required]
        public long TutorialId { get; set; }
        public Tutorial? Tutorial { get; set; }

        /// <summary>gama-api's <c>tutorials_files.id</c>, so a second copy updates instead of duplicating.</summary>
        [Column(nameof(CoreFileId), DataType.Long)]
        [Required]
        public long CoreFileId { get; set; }

        [Column(nameof(Path), DataType.String)]
        [StringLength(200)]
        [Required]
        public string? Path { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<TutorialImage> builder)
        {
            _ = builder.HasOne(t => t.Tutorial).WithMany(t => t.Images).HasForeignKey(t => t.TutorialId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasIndex(t => t.CoreFileId).IsUnique();
        }
    }
}
