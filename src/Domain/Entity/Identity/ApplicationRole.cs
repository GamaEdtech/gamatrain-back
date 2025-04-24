namespace GamaEdtech.Domain.Entity.Identity
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(ApplicationRole))]
    public class ApplicationRole : IdentityRole<int>, IEntity<ApplicationRole, int>
    {
        public ApplicationRole()
        {
            UserRoles = [];
            RoleClaims = [];
        }

        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Int)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public override int Id { get; set; }

        [Column(nameof(Name), DataType.UnicodeString)]
        [Required]
        [StringLength(256)]
        public override string? Name { get; set; }

        [Column(nameof(NormalizedName), DataType.UnicodeString)]
        [StringLength(256)]
        [Required]
        public override string? NormalizedName { get; set; }

        [Column(nameof(ConcurrencyStamp), DataType.String)]
        [Required]
        [StringLength(50)]
        public override string? ConcurrencyStamp { get; set; }

        public ICollection<ApplicationUserRole> UserRoles { get; set; }

        public ICollection<ApplicationRoleClaim> RoleClaims { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ApplicationRole> builder) => _ = builder.HasIndex(t => t.NormalizedName)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationRole)}_{nameof(NormalizedName)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(NormalizedName), pluralize: false)}] IS NOT NULL)");
    }
}
