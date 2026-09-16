namespace GamaEdtech.Presentation.ViewModel.Payment
{
    public sealed class PaymentsSummaryResponseViewModel
    {
        public DateOnly Date { get; set; }
        public decimal PendingAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal FailedAmount { get; set; }
        public long FailedCount { get; set; }
        public long PaidCount { get; set; }
        public long PendingCount { get; set; }

        /// <summary>
        /// Per-Kind breakdown (see <see cref="GamaEdtech.Domain.Enumeration.PaymentKind"/>), summed across every
        /// <see cref="GamaEdtech.Domain.Enumeration.PaymentStatus"/> for this date - an independent pivot of the
        /// same underlying data as the Status-based fields above, not a subset of <see cref="PaidAmount"/>.
        /// Null-Kind rows (payments recorded before this column existed) are excluded from these fields, so
        /// PaidAmount can be larger than the sum of the four *Amount fields below for older date ranges.
        /// </summary>
        public decimal NewSubscriptionAmount { get; set; }
        public long NewSubscriptionCount { get; set; }
        public decimal RenewalAmount { get; set; }
        public long RenewalCount { get; set; }
        public decimal PlanSwitchAmount { get; set; }
        public long PlanSwitchCount { get; set; }
        public decimal PointsTopUpAmount { get; set; }
        public long PointsTopUpCount { get; set; }
    }
}
