namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Lifecycle of a CommissionPayout: Pending -> Approved -> Paid, or Pending -> Rejected/Cancelled
    /// (an Approved request can also still be Rejected, e.g. when the transfer turns out to be impossible).
    /// Pending and Approved are the "open" states: they hold the requested amount out of the owner's
    /// available balance, and at most one open request may exist per owner (filtered unique index on
    /// CommissionPayout). The values are referenced by that index's SQL filter - never renumber them.
    /// </summary>
    public sealed class PayoutStatus : Enumeration<PayoutStatus, byte>
    {
        [Display]
        public static readonly PayoutStatus Pending = new(nameof(Pending), 0);

        [Display]
        public static readonly PayoutStatus Approved = new(nameof(Approved), 1);

        [Display]
        public static readonly PayoutStatus Paid = new(nameof(Paid), 2);

        [Display]
        public static readonly PayoutStatus Rejected = new(nameof(Rejected), 3);

        [Display]
        public static readonly PayoutStatus Cancelled = new(nameof(Cancelled), 4);

        public PayoutStatus()
        {
        }

        public PayoutStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
