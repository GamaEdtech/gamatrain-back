namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    public sealed class SchoolMapTileResponseViewModel
    {
        /// <summary>Every matching school in the tile, whether listed one by one or counted in clusters.</summary>
        public int Total { get; set; }

        public IEnumerable<SchoolMapClusterViewModel>? Clusters { get; set; }

        public IEnumerable<SchoolMapPointViewModel>? Schools { get; set; }
    }

    public sealed class SchoolMapClusterViewModel
    {
        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lng")]
        public double Longitude { get; set; }

        public int Count { get; set; }
    }

    public sealed class SchoolMapPointViewModel
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        public string? Slug { get; set; }

        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lng")]
        public double Longitude { get; set; }

        public double? Rating { get; set; }

        public int ReviewCount { get; set; }

        public bool HasImage { get; set; }
    }
}
