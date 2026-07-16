namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SchoolInfoRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public int? CountryId { get; set; }

        [Display]
        public int? StateId { get; set; }

        [Display]
        public int? CityId { get; set; }

        [Display]
        public LocationViewModel? Location { get; set; }

        [Display]
        public string? Name { get; set; }

        [Display]
        public bool? HasScore { get; set; }

        [Display]
        public bool? HasImage { get; set; }

        [Display]
        public RangeViewModel<decimal>? Tuition { get; set; }

        [Display]
        public IEnumerable<int>? Boards { get; set; }
    }
}
