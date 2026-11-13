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
    /// One row per user and week the weekly report job handled: the report, or the reminder sent instead for a week with
    /// no activity. The unique (UserId, WeekStart) index is what makes a rerun or a retried run safe: the row is written
    /// before the email goes out, so a second run can't send the same week again. See docs/business/notifications.md,
    /// "Weekly report".
    /// </summary>
    [Table(nameof(WeeklyReportLog))]
    public class WeeklyReportLog : IEntity<WeeklyReportLog, long>, IUserId<long>, ICreationDate
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

        /// <summary>Monday 00:00 UTC of the week the email is about.</summary>
        [Column(nameof(WeekStart), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset WeekStart { get; set; }

        [Column(nameof(Kind), DataType.Byte)]
        [Required]
        public WeeklyReportKind Kind { get; set; } = WeeklyReportKind.Report;

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        /// <summary>When the email was handed to the email provider; null while it is being sent.</summary>
        [Column(nameof(SentDate), DataType.DateTimeOffset)]
        public DateTimeOffset? SentDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<WeeklyReportLog> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.WeekStart }).IsUnique();
            _ = builder.HasIndex(t => new { t.WeekStart, t.Kind });
            _ = builder.OwnEnumeration<WeeklyReportLog, WeeklyReportKind, byte>(t => t.Kind);
        }
    }
}
