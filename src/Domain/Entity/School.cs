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

    using NetTopologySuite.Geometries;

    [Table(nameof(School))]
    public class School : VersionableEntity<ApplicationUser, long, long?>, IEntity<School, long>, IDeletable, IContentLocalizeable
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(OsmId), DataType.Long)]
        public long? OsmId { get; set; }

        [Column(nameof(Name), DataType.UnicodeString)]
        [StringLength(300)]
        [Required]
        public string? Name { get; set; }

        [Column(nameof(LocalName), DataType.UnicodeString)]
        [StringLength(300)]
        public string? LocalName { get; set; }

        [Column(nameof(SchoolType), DataType.Byte)]
        public SchoolType? SchoolType { get; set; }

        [Column(nameof(CountryId), DataType.Int)]
        public int? CountryId { get; set; }
        public Location? Country { get; set; }

        [Column(nameof(StateId), DataType.Int)]
        public int? StateId { get; set; }
        public Location? State { get; set; }

        [Column(nameof(CityId), DataType.Int)]
        public int? CityId { get; set; }
        public Location? City { get; set; }

        [Column(nameof(ZipCode), DataType.UnicodeString)]
        [StringLength(100)]
        public string? ZipCode { get; set; }

        [Column(nameof(Address), DataType.UnicodeMaxString)]
        public string? Address { get; set; }

        [Column(nameof(LocalAddress), DataType.UnicodeMaxString)]
        public string? LocalAddress { get; set; }

        [Column(nameof(Coordinates), TypeName = "geography")]
        public Point? Coordinates { get; set; }

        [Column(nameof(Quarter), DataType.UnicodeString)]
        [StringLength(100)]
        public string? Quarter { get; set; }

        [Column(nameof(FaxNumber), DataType.UnicodeString)]
        [StringLength(200)]
        public string? FaxNumber { get; set; }

        [Column(nameof(PhoneNumber), DataType.UnicodeString)]
        [StringLength(200)]
        public string? PhoneNumber { get; set; }

        [Column(nameof(Email), DataType.UnicodeString)]
        [StringLength(200)]
        public string? Email { get; set; }

        [Column(nameof(WebSite), DataType.UnicodeString)]
        [StringLength(300)]
        public string? WebSite { get; set; }

        [Column(nameof(Score), TypeName = "float")]
        public double? Score { get; set; }

        [Column(nameof(IsDeleted), DataType.Boolean)]
        public bool IsDeleted { get; set; }

        [Column(nameof(Tuition), DataType.Decimal)]
        public decimal? Tuition { get; set; }

        [Column(nameof(DefaultImageId), DataType.Long)]
        public long? DefaultImageId { get; set; }
        public SchoolImage? DefaultImage { get; set; }

        [Column(nameof(Description), DataType.UnicodeMaxString)]
        public string? Description { get; set; }

        [Column(nameof(ViewCount), DataType.Long)]
        public long ViewCount { get; set; }

        [Column(nameof(CountryRank), DataType.Int)]
        public int? CountryRank { get; set; }

        [Column(nameof(StateRank), DataType.Int)]
        public int? StateRank { get; set; }

        [Column(nameof(CityRank), DataType.Int)]
        public int? CityRank { get; set; }

        public virtual ICollection<SchoolComment> SchoolComments { get; set; } = [];
        public virtual ICollection<SchoolTag> SchoolTags { get; set; } = [];
        public virtual ICollection<SchoolImage> SchoolImages { get; set; } = [];
        public virtual ICollection<SchoolBoard> SchoolBoards { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<School> builder)
        {
            _ = builder.OwnEnumeration<School, SchoolType, byte>(t => t.SchoolType);
            _ = builder.HasOne(t => t.Country).WithMany().HasForeignKey(t => t.CountryId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.State).WithMany().HasForeignKey(t => t.StateId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.City).WithMany().HasForeignKey(t => t.CityId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.DefaultImage).WithMany().HasForeignKey(t => t.DefaultImageId).OnDelete(DeleteBehavior.NoAction);

            _ = builder.HasQueryFilter(t => !t.IsDeleted).HasIndex(t => t.IsDeleted);

            _ = builder.HasIndex(t => t.Score).IsDescending(true);
            _ = builder.HasIndex(t => new { t.LastModifyDate, t.CreationDate }).IsDescending(true, true);

            _ = builder.HasIndex(t => new { t.IsDeleted, t.Name });
        }
    }
}
