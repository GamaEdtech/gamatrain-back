namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SetPlanFeaturesRequestViewModel
    {
        [Display]
        public IEnumerable<PlanFeatureItemViewModel>? Features { get; set; }
    }

    public sealed class PlanFeatureItemViewModel
    {
        [Display]
        public int FeatureId { get; set; }

        /// <summary><see langword="null"/> means unlimited.</summary>
        [Display]
        public int? Limit { get; set; }
    }
}
