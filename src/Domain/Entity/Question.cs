namespace GamaEdtech.Domain.Entity
{
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(Question))]
    public class Question : VersionableEntity<ApplicationUser, int, int?>, IEntity<Question, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(Body), DataType.UnicodeMaxString)]
        [Required]
        public string? Body { get; set; }

        [Column(nameof(Options), DataType.UnicodeMaxString)]
        [Required]
        public Collection<QuestionOption> Options { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<Question> builder) => _ = builder.OwnsMany(t => t.Options, b => b.ToJson());
    }
}
