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
    /// A user's connection to a school, as the user stated it (2026-11-11). A user can belong to several schools, and to
    /// one school in several roles (a teacher whose child studies there). See docs/business/schools-directory.md,
    /// "School members".
    /// </summary>
    [Table(nameof(SchoolMember))]
    public class SchoolMember : IEntity<SchoolMember, long>, IUserId<long>, ISchoolId
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

        [Column(nameof(SchoolId), DataType.Long)]
        [Required]
        public long SchoolId { get; set; }
        public School? School { get; set; }

        [Column(nameof(Role), DataType.Byte)]
        [Required]
        public SchoolMemberRole Role { get; set; } = SchoolMemberRole.Student;

        /// <summary>The member agreed to be named on the school page. Only used for teachers and staff.</summary>
        [Column(nameof(ShowName), DataType.Boolean)]
        [Required]
        public bool ShowName { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SchoolMember> builder)
        {
            _ = builder.OwnEnumeration<SchoolMember, SchoolMemberRole, byte>(t => t.Role);
            _ = builder.HasIndex(t => new { t.UserId, t.SchoolId, t.Role }).IsUnique();
            _ = builder.HasIndex(t => new { t.SchoolId, t.Role });
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne(t => t.School).WithMany().HasForeignKey(t => t.SchoolId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
