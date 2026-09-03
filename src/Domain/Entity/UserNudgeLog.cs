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

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// Tracks nudges already sent to a user, one row per (UserId, NudgeType) - what makes it safe for
    /// NudgeService.EvaluateAndSendNudgesAsync to run on a recurring schedule without spamming: enforces
    /// a cooldown between resends and a cap on total sends (see NudgeService for the exact numbers), and
    /// the eligibility check re-runs every time before a resend, so a user who resolves the underlying
    /// condition (e.g. sets their avatar) between runs never gets nudged for it again. See
    /// docs/business/notifications.md, "Nudge system".
    /// </summary>
    [Table(nameof(UserNudgeLog))]
    public class UserNudgeLog : IEntity<UserNudgeLog, long>
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

        [Column(nameof(NudgeType), DataType.Byte)]
        [Required]
        public NudgeType? NudgeType { get; set; }

        [Column(nameof(LastSentDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset LastSentDate { get; set; }

        [Column(nameof(SendCount), DataType.Int)]
        [Required]
        public int SendCount { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<UserNudgeLog> builder)
        {
            _ = builder.HasIndex(t => new { t.UserId, t.NudgeType }).IsUnique();
            _ = builder.OwnEnumeration<UserNudgeLog, NudgeType, byte>(t => t.NudgeType);
        }
    }
}
