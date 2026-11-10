namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>The school list's filters, shared by the list, its facet counts and the map tiles.</summary>
    public class SchoolFilterRequestViewModel
    {
        [Display]
        public int? CountryId { get; set; }

        [Display]
        public int? StateId { get; set; }

        [Display]
        public int? CityId { get; set; }

        [Display]
        public LocationViewModel? Location { get; set; }

        /// <summary>The visible map area. Unlike <see cref="Location"/> it matches exactly what the map shows.</summary>
        [Display]
        public BoundsViewModel? Bounds { get; set; }

        [Display]
        public string? Name { get; set; }

        [Display]
        public bool? HasRating { get; set; }

        [Display]
        public bool? HasImage { get; set; }

        [Display]
        public RangeViewModel<decimal>? Tuition { get; set; }

        [Display]
        public IEnumerable<int>? Boards { get; set; }

        /// <summary>School types by name or value (Public, Private, ...); a school of any of them matches.</summary>
        [Display]
        public IEnumerable<string>? SchoolTypes { get; set; }

        /// <summary>Facility tag ids; a school must have all of them.</summary>
        [Display]
        public IEnumerable<long>? Tags { get; set; }
    }
}
