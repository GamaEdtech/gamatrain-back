namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(Feature))]
    public class Feature : IEntity<Feature, int>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Int)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public int Id { get; set; }

        [Column(nameof(Code), DataType.String)]
        [StringLength(50)]
        [Required]
        public string? Code { get; set; }

        [Column(nameof(Name), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? Name { get; set; }

        [Column(nameof(Description), DataType.UnicodeString)]
        [StringLength(500)]
        public string? Description { get; set; }

        [Column(nameof(IsActive), DataType.Boolean)]
        [Required]
        public bool IsActive { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Feature> builder) => _ = builder.HasIndex(t => t.Code).IsUnique();
    }
}
