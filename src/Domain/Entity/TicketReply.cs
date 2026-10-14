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

    [Table(nameof(TicketReply))]
    public class TicketReply : IEntity<TicketReply, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(TicketId), DataType.Long)]
        public long TicketId { get; set; }
        public Ticket Ticket { get; set; }

        [Column(nameof(CreationUserId), DataType.Long)]
        public long? CreationUserId { get; set; }
        public ApplicationUser? CreationUser { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(Body), DataType.UnicodeMaxString)]
        [Required]
        public string? Body { get; set; }

        [Column(nameof(IsRead), DataType.Boolean)]
        public bool IsRead { get; set; }

        [Column(nameof(IsReadByAdmin), DataType.Boolean)]
        public bool IsReadByAdmin { get; set; }

        /// <summary>Written by our side (through the admin tickets endpoint), not by the customer or an inbound email.
        /// Stored when the reply is made, so it doesn't change if the writer later loses a role or is removed.</summary>
        [Column(nameof(BySupport), DataType.Boolean)]
        public bool BySupport { get; set; }

        [Column(nameof(FileId), DataType.String)]
        [StringLength(100)]
        public string? FileId { get; set; }

        [Column(nameof(Receivers), DataType.UnicodeString)]
        [StringLength(500)]
        public ICollection<string?>? Receivers { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<TicketReply> builder)
        {
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
