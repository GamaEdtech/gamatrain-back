namespace GamaEdtech.Domain.Specification.School
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    using NetTopologySuite;
    using NetTopologySuite.Geometries;

    /// <summary>
    /// Schools inside a latitude/longitude box: the visible map area, or one map tile. Unlike the radius search it matches
    /// exactly what the map shows, and the same box gives the same query, so map requests can be cached.
    /// </summary>
    /// <remarks>
    /// The exact test is a plain latitude/longitude range. A small box also gets a geography polygon test in front of it,
    /// which lets SQL Server use the spatial index; the polygon is padded because geography edges are great-circle arcs, not
    /// lines of latitude, so an unpadded polygon would miss schools near its south (north, below the equator) edge. A box
    /// whose west edge is east of its east edge (crossing the antimeridian) is searched as the whole longitude range.
    /// </remarks>
    public sealed class WithinBoundsSpecification : SpecificationBase<School>
    {
        // Above this span (degrees) the polygon's arcs bend too far from the box and the index helps little anyway.
        private const double MaxIndexedSpan = 10;

        private readonly double south;
        private readonly double west;
        private readonly double north;
        private readonly double east;
        private readonly Polygon? area;

        public WithinBoundsSpecification(double south, double west, double north, double east)
        {
            this.south = Math.Clamp(Math.Min(south, north), -90, 90);
            this.north = Math.Clamp(Math.Max(south, north), -90, 90);
            west = Math.Clamp(west, -180, 180);
            east = Math.Clamp(east, -180, 180);
            if (west > east)
            {
                west = -180;
                east = 180;
            }

            this.west = west;
            this.east = east;

            var latSpan = this.north - this.south;
            var lngSpan = this.east - this.west;
            if (Math.Max(latSpan, lngSpan) <= MaxIndexedSpan && this.north < 85 && this.south > -85)
            {
                var pad = (Math.Max(latSpan, lngSpan) * 0.1) + 0.01;
                var s = Math.Max(this.south - pad, -89);
                var n = Math.Min(this.north + pad, 89);
                var w = Math.Max(this.west - pad, -179.999);
                var e = Math.Min(this.east + pad, 179.999);

                // Counter-clockwise, the ring orientation SQL Server geography reads as "the inside is this box".
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                area = factory.CreatePolygon([new(w, s), new(e, s), new(e, n), new(w, n), new(w, s)]);
            }
        }

        public override Expression<Func<School, bool>> Expression()
        {
            // North and east edges are exclusive, so a school on the edge between two map tiles is counted in one of them, not
            // both. The world's own north and east edges stay inclusive.
            var s = south;
            var w = west;
            var n = north >= 85 ? 91 : north;
            var e = east >= 180 ? 181 : east;
            var polygon = area;
            return polygon is null
                ? (t) => t.Coordinates != null && t.Coordinates.Y >= s && t.Coordinates.Y < n && t.Coordinates.X >= w && t.Coordinates.X < e
                : (t) => t.Coordinates != null && t.Coordinates.Intersects(polygon)
                    && t.Coordinates.Y >= s && t.Coordinates.Y < n && t.Coordinates.X >= w && t.Coordinates.X < e;
        }
    }
}
