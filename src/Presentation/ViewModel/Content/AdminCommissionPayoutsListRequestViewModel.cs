namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class AdminCommissionPayoutsListRequestViewModel : CommissionPayoutsListRequestViewModel
    {
        /// <summary>Optional - one owner's payouts.</summary>
        [Display]
        public long? UserId { get; set; }
    }
}
