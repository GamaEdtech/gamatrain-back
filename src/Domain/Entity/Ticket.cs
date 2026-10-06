namespace GamaEdtech.Domain.Entity
{
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Unicode;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(Ticket))]
    public class Ticket : IEntity<Ticket, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        public long? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        /// <summary>Newest of the ticket's own CreationDate and its replies' - stamped on create and on every reply,
        /// so the admin list can sort by latest activity from an index instead of aggregating replies per row.</summary>
        [Column(nameof(LastActivityDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset LastActivityDate { get; set; }

        [Column(nameof(FullName), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? FullName { get; set; }

        [Column(nameof(Email), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? Email { get; set; }

        [Column(nameof(Subject), DataType.UnicodeString)]
        [StringLength(500)]
        [Required]
        public string? Subject { get; set; }

        [Column(nameof(Receivers), DataType.UnicodeString)]
        [StringLength(500)]
        public ICollection<string?>? Receivers { get; set; }

        [Column(nameof(Body), DataType.UnicodeMaxString)]
        [Required]
        public string? Body { get; set; }

        [Column(nameof(IsReadByAdmin), DataType.Boolean)]
        public bool IsReadByAdmin { get; set; }

        [Column(nameof(FileId), DataType.String)]
        [StringLength(100)]
        public string? FileId { get; set; }

        public virtual ICollection<TicketReply> TicketReplys { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<Ticket> builder)
        {
            _ = builder.HasIndex(t => t.Email);
            // Matches the admin sort (latest activity first).
            _ = builder.HasIndex(t => t.LastActivityDate).IsDescending();

            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            };
            _ = builder.Property(t => t.Receivers)
                .HasConversion(
                    t => JsonSerializer.Serialize(t, options),
                    t => JsonSerializer.Deserialize<Collection<string?>?>(t, options)
                );
        }
    }
}
