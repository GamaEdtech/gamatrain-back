namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.UnitOfWork;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The follow button is shown on every content page, including the viewer's own, so following yourself
    /// must be refused before anything is read or written.
    /// </summary>
    public class ConnectionServiceTests
    {
        [Fact]
        public async Task FollowingYourselfIsRefusedWithoutTouchingTheDatabase()
        {
            // Every dependency throws if touched: a self-follow must return before any read or write.
            var service = new ConnectionService(
                new Lazy<IUnitOfWorkProvider>(() => throw new InvalidOperationException("database touched")),
                new(() => throw new InvalidOperationException("http context touched")),
                new(() => throw new InvalidOperationException("localizer touched")),
                new(() => throw new InvalidOperationException("logger touched")),
                new(() => throw new InvalidOperationException("file service touched")));

            var result = await service.FollowAsync(new() { UserId = 7, ProfileId = 7, SubscribeToActivityFeed = true });

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.False(result.Data);
        }
    }
}
