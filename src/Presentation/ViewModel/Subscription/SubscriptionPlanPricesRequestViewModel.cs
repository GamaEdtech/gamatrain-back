namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SubscriptionPlanPricesRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; }

        /// <summary>Optional filter - when set, only this plan's price rows are returned.</summary>
        [Display]
        public long? SubscriptionPlanId { get; set; }
    }
}
