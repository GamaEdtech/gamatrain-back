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

    [Table(nameof(Payment))]
    public class Payment : IEntity<Payment, long>, IUserId<long>, ICreationDate
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

        [Column(nameof(Amount), DataType.Decimal)]
        [Required]
        public decimal Amount { get; set; }

        [Column(nameof(Currency), DataType.Byte)]
        [Required]
        public Currency Currency { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public PaymentStatus Status { get; set; }

        [Column(nameof(Gateway), DataType.Byte)]
        [Required]
        public PaymentGateway Gateway { get; set; }

        /// <summary>
        /// What this payment represents - new subscription/renewal/plan switch/points top-up. Nullable: rows
        /// predating this column (added 2026-09-16) are backfilled only where provably correct (points top-ups,
        /// and a subscription's first payment) - see the adding migration's own comment for why renewal vs. plan
        /// switch is deliberately left null for older rows rather than guessed. Always set for every payment
        /// recorded from this column's introduction onward.
        /// </summary>
        [Column(nameof(Kind), DataType.Byte)]
        public PaymentKind? Kind { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(VerifyDate), DataType.DateTimeOffset)]
        public DateTimeOffset? VerifyDate { get; set; }

        [Column(nameof(SourceWallet), DataType.UnicodeString)]
        [StringLength(200)]
        public string? SourceWallet { get; set; }

        [Column(nameof(Comment), DataType.UnicodeString)]
        [StringLength(100)]
        public string? Comment { get; set; }

        [Column(nameof(TransactionId), DataType.UnicodeString)]
        [StringLength(200)]
        public string? TransactionId { get; set; }

        /// <summary>Set when this payment purchases a subscription; verify then activates the subscription instead of crediting points.</summary>
        [Column(nameof(UserSubscriptionId), DataType.Long)]
        public long? UserSubscriptionId { get; set; }
        public UserSubscription? UserSubscription { get; set; }

        /// <summary>Amount converted to the base reporting currency (USD), locked at verify time.</summary>
        [Column(nameof(BaseCurrencyAmount), DataType.Decimal)]
        public decimal? BaseCurrencyAmount { get; set; }

        [Column(nameof(ExchangeRate), DataType.Decimal)]
        public decimal? ExchangeRate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<Payment> builder)
        {
            _ = builder.Property(t => t.Amount).HasPrecision(36, 18);
            _ = builder.Property(t => t.BaseCurrencyAmount).HasPrecision(36, 18);
            _ = builder.Property(t => t.ExchangeRate).HasPrecision(36, 18);
            _ = builder.OwnEnumeration<Payment, Currency, byte>(t => t.Currency);
            _ = builder.OwnEnumeration<Payment, PaymentStatus, byte>(t => t.Status);
            _ = builder.OwnEnumeration<Payment, PaymentGateway, byte>(t => t.Gateway);
            _ = builder.OwnEnumeration<Payment, PaymentKind, byte>(t => t.Kind);
            _ = builder.HasOne(t => t.UserSubscription).WithMany(t => t.Payments).HasForeignKey(t => t.UserSubscriptionId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.TransactionId, t.Gateway }).IsUnique();
        }
    }
}
