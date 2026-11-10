namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SchoolFacetsRequestViewModel : SchoolFilterRequestViewModel
    {
        /// <summary>Also count the schools per country, state or city one level below the location filter.</summary>
        [Display]
        public bool IncludeRegions { get; set; }
    }
}
