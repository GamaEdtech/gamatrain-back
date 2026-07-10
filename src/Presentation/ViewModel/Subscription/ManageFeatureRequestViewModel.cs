namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ManageFeatureRequestViewModel
    {
        [Display]
        public string? Code { get; set; }

        [Display]
        public string? Name { get; set; }

        [Display]
        public string? Description { get; set; }

        [Display]
        public bool? IsActive { get; set; }
    }
}
