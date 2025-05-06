namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(Contact))]
    public class Contact : IEntity<Contact, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(FullName), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? FullName { get; set; }

        [Column(nameof(Email), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? Email { get; set; }

        [Column(nameof(Subject), DataType.UnicodeString)]
        [StringLength(200)]
        [Required]
        public string? Subject { get; set; }

        [Column(nameof(Body), DataType.UnicodeMaxString)]
        [Required]
        public string? Body { get; set; }

        [Column(nameof(IsRead), DataType.Boolean)]
        public bool IsRead { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Contact> builder)
        {
        }
    }
}
