namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Exam;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Domain.Specification.PastPaper;
    using GamaEdtech.Domain.Specification.Post;

    using Xunit;

    public sealed class PublicProfileAccessTests
    {
        private static readonly Func<Exam, bool> PublishedExam = new PublishedExamSpecification().Expression().Compile();
        private static readonly Func<PastPaper, bool> PublishedPaper = new PublishedPastPaperSpecification().Expression().Compile();
        private static readonly Func<Post, bool> PublishedPost = new PublishedPostSpecification()
            .AndNot(new VisibilityTypeEqualsSpecification(VisibilityType.Private)).Expression().Compile();
        private static readonly Func<Connection, bool> ConfirmedConnection = new FollowerIdEqualsSpecification(10)
            .And(new FollowingIdEqualsSpecification(20)).Expression().Compile();

        [Fact]
        public void PrivateProfileIsHiddenFromAnotherUserEvenWhenConnected() =>
            Assert.False(IdentityService.IsProfileVisible(ProfileVisibility.Private, owner: false, connected: true));

        [Fact]
        public void ConnectionsOnlyProfileIsHiddenWithoutAConfirmedConnection() =>
            Assert.False(IdentityService.IsProfileVisible(ProfileVisibility.ConnectionsOnly, owner: false, connected: false));

        [Fact]
        public void ConnectionsOnlyProfileIsVisibleWithAConfirmedConnection() =>
            Assert.True(IdentityService.IsProfileVisible(ProfileVisibility.ConnectionsOnly, owner: false, connected: true));

        [Fact]
        public void OnlyAConfirmedConnectionInTheViewerToProfileDirectionGrantsAccess()
        {
            Assert.True(ConfirmedConnection(new() { SourceUserId = 10, DestinationUserId = 20, Status = ConnectionStatus.Confirmed }));
            Assert.False(ConfirmedConnection(new() { SourceUserId = 10, DestinationUserId = 20, Status = ConnectionStatus.Requested }));
            Assert.False(ConfirmedConnection(new() { SourceUserId = 20, DestinationUserId = 10, Status = ConnectionStatus.Confirmed }));
        }

        [Theory]
        [MemberData(nameof(AllVisibilities))]
        public void OwnerCanSeeTheirOwnProfile(ProfileVisibility visibility) =>
            Assert.True(IdentityService.IsProfileVisible(visibility, owner: true, connected: false));

        [Fact]
        public void StudentBoardCannotBeInferredFromRelatedTeachers()
        {
            Assert.False(IdentityService.CanShowRelatedTeachers(isTeacher: false, board: 12));
            Assert.False(IdentityService.CanShowRelatedTeachers(isTeacher: true, board: null));
            Assert.True(IdentityService.CanShowRelatedTeachers(isTeacher: true, board: 12));
        }

        [Theory]
        [InlineData((byte)1)]
        [InlineData((byte)5)]
        [InlineData((byte)7)]
        public void PublicExamStatusesAreIncluded(byte status) =>
            Assert.True(PublishedExam(new() { Status = status }));

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)2)]
        [InlineData((byte)3)]
        [InlineData((byte)4)]
        [InlineData((byte)6)]
        [InlineData((byte)8)]
        public void NonPublicExamStatusesAreExcluded(byte status) =>
            Assert.False(PublishedExam(new() { Status = status }));

        [Fact]
        public void DeletedPublicExamIsExcluded() =>
            Assert.False(PublishedExam(new() { Status = 1, IsDeleted = true }));

        [Theory]
        [MemberData(nameof(UnpublishedPaperStatuses))]
        public void NonConfirmedPastPaperStatusesAreExcluded(PastPaperStatus status) =>
            Assert.False(PublishedPaper(new() { Status = status }));

        [Fact]
        public void DeletedConfirmedPastPaperIsExcluded() =>
            Assert.False(PublishedPaper(new() { Status = PastPaperStatus.Confirmed, IsDeleted = true }));

        [Fact]
        public void DraftFutureAndPrivatePostsAreExcluded()
        {
            Assert.False(PublishedPost(new() { Status = Status.Draft, PublishDate = DateTimeOffset.UtcNow.AddMinutes(-1), VisibilityType = VisibilityType.General }));
            Assert.False(PublishedPost(new() { Status = Status.Confirmed, PublishDate = DateTimeOffset.UtcNow.AddMinutes(1), VisibilityType = VisibilityType.General }));
            Assert.False(PublishedPost(new() { Status = Status.Confirmed, PublishDate = DateTimeOffset.UtcNow.AddMinutes(-1), VisibilityType = VisibilityType.Private }));
        }

        [Fact]
        public void PremiumPostsRemainDiscoverableInProfilePreviews() =>
            Assert.True(PublishedPost(new() { Status = Status.Confirmed, PublishDate = DateTimeOffset.UtcNow.AddMinutes(-1), VisibilityType = VisibilityType.Premium }));

        public static TheoryData<ProfileVisibility> AllVisibilities => new()
        {
            ProfileVisibility.Private,
            ProfileVisibility.Public,
            ProfileVisibility.ConnectionsOnly,
        };

        public static TheoryData<PastPaperStatus> UnpublishedPaperStatuses => new()
        {
            PastPaperStatus.Pending,
            PastPaperStatus.Rejected,
            PastPaperStatus.NeedsChanges,
            PastPaperStatus.Deactivated,
            PastPaperStatus.Deleted,
            PastPaperStatus.Edited,
        };
    }
}
