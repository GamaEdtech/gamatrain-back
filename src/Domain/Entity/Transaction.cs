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

    [Table(nameof(Transaction))]
    public class Transaction : IEntity<Transaction, long>, IUserId<long>, IIdentifierId, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(PreviousTransactionId), DataType.Long)]
        public long? PreviousTransactionId { get; set; }
        public Transaction? PreviousTransaction { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(IdentifierId), DataType.Long)]
        public long? IdentifierId { get; set; }

        [Column(nameof(Points), DataType.Long)]
        [Required]
        public long Points { get; set; }

        [Column(nameof(Description), DataType.UnicodeString)]
        [StringLength(300)]
        public string? Description { get; set; }

        [Column(nameof(CurrentBalance), DataType.Long)]
        [Required]
        public long CurrentBalance { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(IsDebit), DataType.Boolean)]
        public bool IsDebit { get; set; }

        [Column(nameof(TransactionType), DataType.Short)]
        public TransactionType TransactionType { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Transaction> builder)
        {
            _ = builder.HasIndex(t => t.PreviousTransactionId).IsUnique(true);
            _ = builder.HasIndex(t => t.TransactionType);
            _ = builder.OwnEnumeration<Transaction, TransactionType, short>(t => t.TransactionType);
        }
    }
}
