namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class GatewayMappingsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; }
    }
}
