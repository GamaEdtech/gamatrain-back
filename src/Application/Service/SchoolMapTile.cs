namespace GamaEdtech.Application.Service
{
    /// <summary>
    /// The latitude/longitude box of a web-map tile (the z/x/y scheme Leaflet and OpenStreetMap use). The school map asks for
    /// schools tile by tile, so every visitor looking at the same place asks the same URLs and the edge cache answers them.
    /// </summary>
    public static class SchoolMapTile
    {
        /// <summary>Deeper than street level adds nothing for schools and multiplies the number of distinct tiles.</summary>
        public const int MaxZoom = 18;

        public static bool IsValid(int zoom, int x, int y) => zoom is >= 0 and <= MaxZoom && x >= 0 && y >= 0 && x < (1 << zoom) && y < (1 << zoom);

        public static (double South, double West, double North, double East) Bounds(int zoom, int x, int y)
        {
            var n = (double)(1 << zoom);
            return (Latitude(y + 1, n), (x / n * 360) - 180, Latitude(y, n), ((x + 1) / n * 360) - 180);
        }

        private static double Latitude(int y, double n) => Math.Atan(Math.Sinh(Math.PI * (1 - (2 * y / n)))) * 180 / Math.PI;
    }
}
