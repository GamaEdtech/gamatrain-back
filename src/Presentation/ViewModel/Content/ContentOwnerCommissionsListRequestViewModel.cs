namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public class ContentOwnerCommissionsListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }
    }
}
