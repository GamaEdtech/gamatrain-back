namespace GamaEdtech.Presentation.ViewModel.Location
{
    public sealed class LocationSearchResponseViewModel
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        /// <summary><c>country</c>, <c>state</c> or <c>city</c>.</summary>
        public string? Type { get; set; }

        public int? StateId { get; set; }

        public string? StateTitle { get; set; }

        public int? CountryId { get; set; }

        public string? CountryTitle { get; set; }
    }
}
