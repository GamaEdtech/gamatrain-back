namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SchoolInfoRequestViewModel : SchoolFilterRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };
    }
}
