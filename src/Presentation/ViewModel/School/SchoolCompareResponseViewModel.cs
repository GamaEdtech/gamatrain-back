namespace GamaEdtech.Presentation.ViewModel.School
{
    public sealed class SchoolCompareResponseViewModel
    {
        public required SchoolResponseViewModel School { get; set; }

        /// <summary>Parents' average rating per topic; null for a school nobody has rated.</summary>
        public SchoolRateResponseViewModel? Rate { get; set; }
    }
}
