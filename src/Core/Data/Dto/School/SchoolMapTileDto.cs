namespace GamaEdtech.Data.Dto.School
{
    /// <summary>
    /// The schools in one map tile: every school when there are few enough to show one by one, otherwise counts per area
    /// of the tile (clusters). <see cref="Total"/> is always the exact number of matching schools in the tile.
    /// </summary>
    public sealed class SchoolMapTileDto
    {
        public int Total { get; set; }
        public IEnumerable<SchoolMapClusterDto>? Clusters { get; set; }
        public IEnumerable<SchoolMapPointDto>? Schools { get; set; }
    }

    public sealed class SchoolMapClusterDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Count { get; set; }
    }

    public sealed class SchoolMapPointDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Rating { get; set; }
        public int ReviewCount { get; set; }
        public bool HasImage { get; set; }
    }
}
