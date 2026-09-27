namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A user's paid export of a gama-api exam in one format (<c>GET exams/export</c>, 2026-09-27). One row per
    /// user + exam + format -- the unique index makes a later export of the same exam in the same format free, and
    /// turns a concurrent duplicate purchase into an insert failure whose charge is refunded (see ExamService).
    /// </summary>
    [Table(nameof(ExamExportPurchase))]
    public class ExamExportPurchase : IEntity<ExamExportPurchase, long>, ICreationDate
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

        /// <summary>The exam's gama-api id.</summary>
        [Column(nameof(ExamId), DataType.Long)]
        [Required]
        public long ExamId { get; set; }

        [Column(nameof(FileType), DataType.Byte)]
        [Required]
        public ExportFileType FileType { get; set; }

        /// <summary>What was charged: the exam's question count x the format's multiplier, rounded up.</summary>
        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        [Column(nameof(PaidBy), DataType.Byte)]
        [Required]
        public SpendSource PaidBy { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ExamExportPurchase> builder)
        {
            _ = builder.OwnEnumeration<ExamExportPurchase, ExportFileType, byte>(t => t.FileType);
            _ = builder.OwnEnumeration<ExamExportPurchase, SpendSource, byte>(t => t.PaidBy);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.ExamId, t.FileType }).IsUnique();
        }
    }
}
