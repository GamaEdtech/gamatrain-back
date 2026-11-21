namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Board;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Data.Dto.Tag;

    using NetTopologySuite.Geometries;

    using Xunit;

    public sealed class SchoolContributionChangesTests
    {
        private static SchoolDto School() => new()
        {
            Id = 619647,
            Name = "Example School",
            Address = "No. 2, Example Street",
            PhoneNumber = "+60 1/+60 2",
            Email = "office@example.test",
            WebSite = "example.test",
            CityId = 68812,
            Tuition = 4750,
            Coordinates = new Point(103.78058910369874, 1.5570135795738038),
            Tags = [new TagDto { Id = 1 }, new TagDto { Id = 2 }],
            Boards = [new BoardDto { Id = 6627 }],
        };

        [Fact]
        public void AnAddressSavedUnchangedIsNoChange()
        {
            SchoolContributionDto contribution = new() { Address = " No. 2, Example Street " };

            Assert.False(SchoolContributionChanges.Keep(contribution, School(), hasImage: false));
            Assert.Null(contribution.Address);
        }

        [Fact]
        public void AFieldSavedEmptyIsNoChange()
        {
            // Clearing a field sent null or "": confirming would keep the school's value anyway.
            Assert.False(SchoolContributionChanges.Keep(new() { PhoneNumber = null }, School(), hasImage: false));
            Assert.False(SchoolContributionChanges.Keep(new() { WebSite = "  " }, School(), hasImage: false));
            Assert.False(SchoolContributionChanges.Keep(new(), null, hasImage: false));
        }

        [Fact]
        public void OnlyTheChangedValuesAreKept()
        {
            SchoolContributionDto contribution = new()
            {
                Name = "Example School",
                Address = "No. 3, Example Street",
                CityId = 68812,
                Tuition = 5000,
                Latitude = 1.5570135795738038,
                Longitude = 103.78058910369874,
                Tags = [2, 1],
                Boards = [6627, 2],
            };

            Assert.True(SchoolContributionChanges.Keep(contribution, School(), hasImage: false));
            Assert.Equal((null, "No. 3, Example Street", null, 5000m), (contribution.Name, contribution.Address, contribution.CityId, contribution.Tuition));
            Assert.Equal((null, null), (contribution.Latitude, contribution.Longitude));
            Assert.Null(contribution.Tags);
            Assert.Equal([6627, 2], contribution.Boards!);
        }

        [Fact]
        public void AMovedPlaceIsAChangeButHalfAPlaceIsNot()
        {
            SchoolContributionDto moved = new() { Latitude = 1.56, Longitude = 103.78058910369874 };
            Assert.True(SchoolContributionChanges.Keep(moved, School(), hasImage: false));
            Assert.Equal((1.56, 103.78058910369874), (moved.Latitude!.Value, moved.Longitude!.Value));

            SchoolContributionDto half = new() { Latitude = 1.56 };
            Assert.False(SchoolContributionChanges.Keep(half, School(), hasImage: false));
            Assert.Null(half.Latitude);
        }

        [Fact]
        public void RemovingAllTagsIsAChangeOnlyWhenTheSchoolHasSome()
        {
            Assert.True(SchoolContributionChanges.Keep(new() { Tags = [] }, School(), hasImage: false));
            var school = School();
            school.Tags = [];
            Assert.False(SchoolContributionChanges.Keep(new() { Tags = [] }, school, hasImage: false));
        }

        [Fact]
        public void AnImageOrACommentAloneIsAChange()
        {
            Assert.True(SchoolContributionChanges.Keep(new() { Address = "No. 2, Example Street" }, School(), hasImage: true));
            Assert.True(SchoolContributionChanges.Keep(new() { Comment = new() }, School(), hasImage: false));
        }

        [Fact]
        public void ANewSchoolKeepsEveryValueGiven()
        {
            SchoolContributionDto contribution = new() { Name = " New School ", Address = "" };

            Assert.True(SchoolContributionChanges.Keep(contribution, null, hasImage: false));
            Assert.Equal(("New School", null), (contribution.Name, contribution.Address));
        }
    }
}
