namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    using DataType = GamaEdtech.Common.Data.DataType;

    [Table(nameof(ExamAnalysis))]
    public class ExamAnalysis : IEntity<ExamAnalysis, long>, IUserId<long>, ICreationDate
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

        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        [Column(nameof(Result), DataType.UnicodeMaxString)]
        [Required]
        public required string Result { get; set; }

        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamAnalysis> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.ExamId }).IsUnique();
        }
    }
}
