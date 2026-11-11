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
    using GamaEdtech.Domain.Enumeration;

    [Table(nameof(ApplicationRole))]
    public class ApplicationRole : IdentityRole<long>, IEntity<ApplicationRole, long>
    {
        public ApplicationRole()
        {
            UserRoles = [];
            RoleClaims = [];
        }

        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public override long Id { get; set; }

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

        public void Configure([NotNull] EntityTypeBuilder<ApplicationRole> builder)
        {
            _ = builder.HasIndex(t => t.NormalizedName)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationRole)}_{nameof(NormalizedName)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(NormalizedName), pluralize: false)}] IS NOT NULL)");

            List<ApplicationRole> seedData =
            [
                new ApplicationRole { Id = 1, Name = nameof(Role.Admin), NormalizedName = nameof(Role.Admin).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE3", },
                new ApplicationRole { Id = 2, Name = nameof(Role.Teacher), NormalizedName = nameof(Role.Teacher).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE4", },
                new ApplicationRole { Id = 3, Name = nameof(Role.Student), NormalizedName = nameof(Role.Student).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE5", },
                new ApplicationRole { Id = 4, Name = nameof(Role.Advisor), NormalizedName = nameof(Role.Advisor).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE6", },
                new ApplicationRole { Id = 5, Name = nameof(Role.Finance), NormalizedName = nameof(Role.Finance).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE7", },
                new ApplicationRole { Id = 6, Name = nameof(Role.Parent), NormalizedName = nameof(Role.Parent).ToUpperInvariant(), ConcurrencyStamp = "85465B3B-E646-49BC-AAC6-D07C450B3AE8", },
            ];
            _ = builder.HasData(seedData);
        }
    }
}
