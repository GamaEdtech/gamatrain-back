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
    /// A user's account at an external payout provider (2026-10-07): today a Stripe Connect Express account, created when
    /// the owner starts Stripe onboarding. Stripe holds the identity and bank details; we keep only its account id and
    /// whether it can receive money yet (refreshed from Stripe on demand, see CommissionPayoutService). One per user and
    /// method.
    /// </summary>
    [Table(nameof(UserPayoutAccount))]
    public class UserPayoutAccount : IEntity<UserPayoutAccount, long>, ICreationDate, IUserId<long>
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

        [Column(nameof(Method), DataType.Byte)]
        [Required]
        public PayoutMethod Method { get; set; } = PayoutMethod.StripeConnect;

        /// <summary>The provider's account id (Stripe: acct_...).</summary>
        [Column(nameof(ExternalAccountId), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? ExternalAccountId { get; set; }

        /// <summary>ISO 3166-1 alpha-2 country the account was created in (fixed at Stripe once created).</summary>
        [Column(nameof(Country), DataType.UnicodeString)]
        [StringLength(2)]
        [Required]
        public string? Country { get; set; }

        /// <summary>Last known: onboarding finished and the provider accepts transfers to it. Refreshed from the provider.</summary>
        [Column(nameof(PayoutsEnabled), DataType.Boolean)]
        [Required]
        public bool PayoutsEnabled { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<UserPayoutAccount> builder)
        {
            _ = builder.OwnEnumeration<UserPayoutAccount, PayoutMethod, byte>(t => t.Method);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.Method }).IsUnique();
        }
    }
}
