namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// One consumption event, written alongside the guarded quota decrement in
    /// <c>SubscriptionQuotaService.ConsumeQuotaAsync</c>. <see cref="UserSubscriptionQuota"/> only ever holds
    /// a running <c>Used</c> counter (an atomic increment, no history) - this table is the event log behind it,
    /// for per-user consumption history and admin usage reporting/aggregation.
    /// </summary>
    /// <remarks>
    /// Deliberately has no FK to <see cref="Entity.UserSubscriptionQuota"/>: <c>SubscriptionQuotaService.
    /// CreateQuotasAsync</c> deletes and re-snapshots a subscription's quota bucket rows on every plan switch
    /// or re-activation, so a hard FK there would either cascade-delete this history out from under a plan
    /// switch or block the delete outright. <see cref="UserId"/>/<see cref="UserSubscriptionId"/>/
    /// <see cref="FeatureId"/> are enough to answer every reporting question this log exists for, and none of
    /// them are ever deleted by that resnapshot.
    /// </remarks>
    [Table(nameof(SubscriptionQuotaConsumptionLog))]
    public class SubscriptionQuotaConsumptionLog : IEntity<SubscriptionQuotaConsumptionLog, long>, IUserId<long>, IIdentifierId, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }

        /// <summary>Which subscription paid for this consumption - the same subscription can appear across many events as its bucket is drawn down over its billing period.</summary>
        [Column(nameof(UserSubscriptionId), DataType.Long)]
        [Required]
        public long UserSubscriptionId { get; set; }
        public UserSubscription? UserSubscription { get; set; }

        /// <summary>The specific feature consumed - kept distinct even when the bucket pools several features onto one shared limit, so per-feature reporting stays accurate for a pooled bucket.</summary>
        [Column(nameof(FeatureId), DataType.Int)]
        [Required]
        public int FeatureId { get; set; }
        public Feature? Feature { get; set; }

        [Column(nameof(Amount), DataType.Int)]
        [Required]
        public int Amount { get; set; }

        /// <summary>Which content item this consumption was for (e.g. a pastpaper/test/exam id) - optional, mirrors Transaction.IdentifierId. No FK: the id's owning table depends on the feature, same as Transaction's own IdentifierId.</summary>
        [Column(nameof(IdentifierId), DataType.Long)]
        public long? IdentifierId { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionQuotaConsumptionLog> builder)
        {
            _ = builder.HasOne(t => t.UserSubscription).WithMany().HasForeignKey(t => t.UserSubscriptionId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.Feature).WithMany().HasForeignKey(t => t.FeatureId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.CreationDate });
            _ = builder.HasIndex(t => new { t.FeatureId, t.CreationDate });
        }
    }
}
