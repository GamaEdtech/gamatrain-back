namespace GamaEdtech.Data.Dto.School
{
    /// <summary>One school of a side-by-side comparison: its details and its parents' ratings per topic.</summary>
    public sealed class SchoolCompareDto
    {
        /// <summary>How many schools one comparison takes.</summary>
        public const int MaxSchools = 4;

        public required SchoolDto School { get; set; }
        public SchoolRateDto? Rate { get; set; }
    }
}
