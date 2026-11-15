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
    /// A signed-in user's read of a post or listen to its podcast, at most once per user, kind, post and day: the unique index
    /// is what keeps a refresh or a second tab from counting again. Personal reading history, so it only feeds the user's own
    /// report and counts for a post's author, and rows older than a year are deleted daily. See
    /// docs/business/exams-and-content.md, "Reads and listens".
    /// </summary>
    [Table(nameof(UserActivity))]
    public class UserActivity : IEntity<UserActivity, long>, IUserId<long>, ICreationDate
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

        [Column(nameof(Type), DataType.Byte)]
        [Required]
        public UserActivityType Type { get; set; } = UserActivityType.PostRead;

        /// <summary>The post read or whose podcast was played.</summary>
        [Column(nameof(ContentId), DataType.Long)]
        [Required]
        public long ContentId { get; set; }

        /// <summary>00:00 UTC of the day it happened.</summary>
        [Column(nameof(Day), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset Day { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<UserActivity> builder)
        {
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasIndex(t => new { t.UserId, t.Type, t.ContentId, t.Day }).IsUnique();
            _ = builder.HasIndex(t => new { t.UserId, t.Day });
            _ = builder.HasIndex(t => new { t.ContentId, t.Type, t.Day });
            _ = builder.HasIndex(t => t.Day);
            _ = builder.OwnEnumeration<UserActivity, UserActivityType, byte>(t => t.Type);
        }
    }
}
