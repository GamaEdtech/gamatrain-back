namespace GamaEdtech.Domain.Entity.Identity
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    using System.Diagnostics.CodeAnalysis;

    [Table(nameof(ApplicationUserRole))]
    public class ApplicationUserRole : IdentityUserRole<int>, IEntity<ApplicationUserRole, int>, IUserId<int>
    {
        [Required]
        [Column(nameof(UserId), DataType.Int)]
        public override int UserId { get; set; }

        [Required]
        [Column(nameof(RoleId), DataType.Int)]
        public override int RoleId { get; set; }

        public ApplicationRole? Role { get; set; }

        public ApplicationUser? User { get; set; }

        int IIdentifiable<int>.Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Configure([NotNull] EntityTypeBuilder<ApplicationUserRole> builder)
        {
            _ = builder.HasKey(t => new { t.UserId, t.RoleId });

            _ = builder.HasIndex(t => t.RoleId)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationUserRole)}_{nameof(RoleId)}"));

            _ = builder.HasOne(t => t.Role)
                .WithMany(t => t.UserRoles)
                .HasForeignKey(t => t.RoleId);

            _ = builder.HasOne(t => t.User)
                .WithMany(t => t.UserRoles)
                .HasForeignKey(t => t.UserId);
        }
    }
}
