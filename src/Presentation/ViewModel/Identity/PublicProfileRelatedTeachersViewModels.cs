namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PublicProfileRelatedTeachersRequestViewModel
    {
        [Display]
        [Range(1, 20)]
        public int Limit { get; set; } = 10;
    }

    public sealed class PublicProfileRelatedTeachersResponseViewModel
    {
        public int TotalRecordsCount { get; set; }
        public IReadOnlyList<PublicProfileListResponseViewModel> Teachers { get; set; } = [];
    }
}
