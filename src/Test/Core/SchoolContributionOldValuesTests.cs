namespace GamaEdtech.Test.Core
{
    using System.Text.Json;

    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Data.Dto.Tag;
    using GamaEdtech.Domain.Enumeration;

    using NetTopologySuite.Geometries;

    using Xunit;

    public sealed class SchoolContributionOldValuesTests
    {
        [Fact]
        public void OldValuesSurviveBeingStoredInTheContribution()
        {
            SchoolContributionDto contribution = new()
            {
                Email = "new@example.org",
                OldValues = new()
                {
                    Id = 7,
                    SchoolType = SchoolType.Private,
                    Coordinates = new Point(-79.45, 43.81) { SRID = 4326 },
                    CityTitle = "Vaughan",
                    Tags = [new TagDto { Id = 3, Name = "Library", TagType = TagType.School }],
                },
            };

            var stored = JsonSerializer.Deserialize<SchoolContributionDto>(JsonSerializer.Serialize(contribution))!;

            Assert.Null(stored.OldValues!.Email);
            Assert.Equal(-79.45, stored.OldValues.Coordinates!.X);
            Assert.Equal(43.81, stored.OldValues.Coordinates.Y);
            Assert.Equal(SchoolType.Private, stored.OldValues.SchoolType);
            Assert.Equal("Vaughan", stored.OldValues.CityTitle);
            Assert.Equal(TagType.School, stored.OldValues.Tags!.Single().TagType);
        }

        [Fact]
        public void ContributionsStoredBeforeThisChangeHaveNoOldValues()
        {
            var stored = JsonSerializer.Deserialize<SchoolContributionDto>("""{"Email":"a@example.org"}""")!;

            Assert.Null(stored.OldValues);
        }
    }
}
