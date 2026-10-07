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
    /// A content owner's request to be paid out (part of) their accrued ContentOwnerCommission balance
    /// (2026-10-07). The owner requests, an admin approves, then an admin confirms the money was actually
    /// transferred (Paid) - see PayoutStatus. Accrual rows are never touched: available balance =
    /// SUM(ContentOwnerCommission.AmountUsd) - SUM(AmountUsd of this owner's Pending/Approved/Paid payouts).
    /// Each admin step records who did it and when (ApprovedBy/PaidBy/RejectedBy), so the row itself is the
    /// audit trail of a payout. The money moves outside this system (a manual bank/PayPal transfer); this
    /// row only tracks the decision and the admin's transfer reference.
    /// </summary>
    [Table(nameof(CommissionPayout))]
    public class CommissionPayout : IEntity<CommissionPayout, long>, ICreationDate, IUserId<long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        /// <summary>The content owner being paid.</summary>
        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>Requested amount, whole cents, reserved out of the available balance while the request is open.</summary>
        [Column(nameof(AmountUsd), DataType.Decimal)]
        [Required]
        public decimal AmountUsd { get; set; }

        /// <summary>Where the money goes: free text as the owner typed it for Manual (account/IBAN/PayPal email...), or "Stripe account acct_..." for StripeConnect.</summary>
        [Column(nameof(Destination), DataType.UnicodeString)]
        [StringLength(500)]
        [Required]
        public string? Destination { get; set; }

        /// <summary>Manual (admin transfers by hand, then marks paid) or StripeConnect (approval sends a Stripe Transfer
        /// to the owner's UserPayoutAccount and marks it paid in the same step). Existing rows are Manual.</summary>
        [Column(nameof(Method), DataType.Byte)]
        [Required]
        public PayoutMethod Method { get; set; } = PayoutMethod.Manual;

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(ApprovedByUserId), DataType.Long)]
        public long? ApprovedByUserId { get; set; }
        public ApplicationUser? ApprovedBy { get; set; }

        [Column(nameof(ApprovalDate), DataType.DateTimeOffset)]
        public DateTimeOffset? ApprovalDate { get; set; }

        /// <summary>The admin who confirmed the money was transferred.</summary>
        [Column(nameof(PaidByUserId), DataType.Long)]
        public long? PaidByUserId { get; set; }
        public ApplicationUser? PaidBy { get; set; }

        [Column(nameof(PaidDate), DataType.DateTimeOffset)]
        public DateTimeOffset? PaidDate { get; set; }

        /// <summary>The transfer's reference: entered by the admin for a Manual payout (bank/PayPal transaction id), or the Stripe transfer id (tr_...) for StripeConnect.</summary>
        [Column(nameof(TransferReference), DataType.UnicodeString)]
        [StringLength(200)]
        public string? TransferReference { get; set; }

        [Column(nameof(RejectedByUserId), DataType.Long)]
        public long? RejectedByUserId { get; set; }
        public ApplicationUser? RejectedBy { get; set; }

        [Column(nameof(RejectionDate), DataType.DateTimeOffset)]
        public DateTimeOffset? RejectionDate { get; set; }

        [Column(nameof(RejectionReason), DataType.UnicodeString)]
        [StringLength(1000)]
        public string? RejectionReason { get; set; }

        [Column(nameof(CancellationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? CancellationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<CommissionPayout> builder)
        {
            _ = builder.Property(t => t.AmountUsd).HasPrecision(18, 2);
            _ = builder.OwnEnumeration<CommissionPayout, PayoutStatus, byte>(t => t.Status);
            _ = builder.OwnEnumeration<CommissionPayout, PayoutMethod, byte>(t => t.Method);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.ApprovedBy).WithMany().HasForeignKey(t => t.ApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.PaidBy).WithMany().HasForeignKey(t => t.PaidByUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.RejectedBy).WithMany().HasForeignKey(t => t.RejectedByUserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.Status });

            // At most one open (Pending = 0 / Approved = 1) request per owner: this is what keeps two concurrent
            // requests from both reserving the same balance - the second insert fails instead of overdrawing.
            _ = builder.HasIndex(t => t.UserId)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(CommissionPayout)}_{nameof(UserId)}_Open"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(Status), pluralize: false)}] IN (0, 1))");
        }
    }
}
