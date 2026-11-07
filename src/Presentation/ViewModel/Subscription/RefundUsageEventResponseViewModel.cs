namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class RefundUsageEventResponseViewModel
    {
        /// <summary>The quota credited back - less than the charge when the bucket's Used was already lower (reset by a renewal since).</summary>
        public int RefundedAmount { get; set; }
    }
}
