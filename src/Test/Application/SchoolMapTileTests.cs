namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.School;

    using NetTopologySuite;

    using Xunit;

    public class SchoolMapTileTests
    {
        private static School At(double latitude, double longitude) => new()
        {
            Coordinates = NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePoint(new NetTopologySuite.Geometries.Coordinate(longitude, latitude)),
        };

        [Fact]
        public void ZoomZeroIsTheWholeMercatorWorld()
        {
            var (south, west, north, east) = SchoolMapTile.Bounds(0, 0, 0);
            Assert.Equal(-180, west, 6);
            Assert.Equal(180, east, 6);
            Assert.Equal(85.0511, north, 3);
            Assert.Equal(-85.0511, south, 3);
        }

        [Fact]
        public void TileHoldingDarEsSalaamContainsIt()
        {
            // Dar es Salaam (-6.79, 39.21) at zoom 12 is tile x 2494, y 2125.
            var (south, west, north, east) = SchoolMapTile.Bounds(12, 2494, 2125);
            Assert.InRange(-6.79, south, north);
            Assert.InRange(39.21, west, east);
        }

        [Theory]
        [InlineData(-1, 0, 0)]
        [InlineData(19, 0, 0)]
        [InlineData(2, 4, 0)]
        [InlineData(2, 0, -1)]
        public void OutOfRangeTilesAreRejected(int zoom, int x, int y) => Assert.False(SchoolMapTile.IsValid(zoom, x, y));

        [Fact]
        public void BoundsMatchOnlyInsideTheBox()
        {
            var matches = new WithinBoundsSpecification(-7, 39, -6.5, 39.5).Expression().Compile();
            Assert.True(matches(At(-6.79, 39.21)));
            Assert.False(matches(At(-7.2, 39.21)));
            Assert.False(matches(At(-6.79, 39.6)));
            Assert.False(matches(new School()));
        }

        [Fact]
        public void SchoolOnASharedTileEdgeIsInOneTileOnly()
        {
            var (south, _, _, east) = SchoolMapTile.Bounds(3, 3, 3);
            var school = At(south, east);
            var hits = 0;
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    var (s, w, n, e) = SchoolMapTile.Bounds(3, x, y);
                    hits += new WithinBoundsSpecification(s, w, n, e).Expression().Compile()(school) ? 1 : 0;
                }
            }

            Assert.Equal(1, hits);
        }

        [Fact]
        public void LargeBoxesUseThePlainRange()
        {
            var matches = new WithinBoundsSpecification(-60, -170, 60, 170).Expression().Compile();
            Assert.True(matches(At(50, 160)));
            Assert.False(matches(At(65, 0)));
        }

        [Fact]
        public void BoxAcrossTheAntimeridianSearchesAllLongitudes()
        {
            var matches = new WithinBoundsSpecification(-20, 170, -10, -170).Expression().Compile();
            Assert.True(matches(At(-15, 0)));
            Assert.False(matches(At(-25, 0)));
        }

        [Fact]
        public void EveryPointOfATileGridFallsInExactlyOneTile()
        {
            // Neighbouring tiles share edges; a school must still be counted once, never twice or never.
            for (var i = 0; i < 200; i++)
            {
                var latitude = -80 + (i * 7.31 % 160);
                var longitude = -179 + (i * 13.7 % 358);
                var school = At(latitude, longitude);
                var hits = 0;
                for (var x = 0; x < 8; x++)
                {
                    for (var y = 0; y < 8; y++)
                    {
                        var (south, west, north, east) = SchoolMapTile.Bounds(3, x, y);
                        if (new WithinBoundsSpecification(south, west, north, east).Expression().Compile()(school))
                        {
                            hits++;
                        }
                    }
                }

                Assert.Equal(1, hits);
            }
        }
    }
}
