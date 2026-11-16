namespace GamaEdtech.Test.Application
{
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The switch between gama-api and the native code of a migrating domain. It must keep traffic on gama-api unless a
    /// setting says otherwise in so many words, and the shadow route must never change what the caller gets.
    /// </summary>
    public sealed class MigrationRouterTests : IDisposable
    {
        private readonly List<ServiceProvider> providers = [];

        private interface IFakeSource : IProvider<MigrationRoute>
        {
            Task<ResultData<string>> GetAsync();
        }

        [Fact]
        public void RouteIsLegacyWithoutASetting() =>
            Assert.Equal(MigrationRoute.Legacy, Router([]).GetRoute(MigrationDomain.Types, "Mcp"));

        [Theory]
        [InlineData("Ntive")]
        [InlineData("")]
        [InlineData("2")]
        public void UnknownSettingIsLegacy(string value) =>
            Assert.Equal(MigrationRoute.Legacy, Router([], ("Migration:Routes:Types", value)).GetRoute(MigrationDomain.Types));

        [Fact]
        public void FeatureSettingWinsOverItsDomain()
        {
            var router = Router([], ("Migration:Routes:Types", "legacy"), ("Migration:Routes:Types.Mcp", " native "));

            Assert.Equal(MigrationRoute.Native, router.GetRoute(MigrationDomain.Types, "Mcp"));
            Assert.Equal(MigrationRoute.Legacy, router.GetRoute(MigrationDomain.Types, "BoardSync"));
            Assert.Equal(MigrationRoute.Legacy, router.GetRoute(MigrationDomain.Exams, "Mcp"));
        }

        [Fact]
        public async Task LegacyRouteNeverTouchesTheNativeSource()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");
            FakeSource native = new(MigrationRoute.Native, "new");

            var result = await Router([legacy, native]).RunAsync<IFakeSource, string>(MigrationDomain.Types, null, t => t.GetAsync());

            Assert.Equal("old", result.Data);
            Assert.Equal((1, 0), (legacy.Calls, native.Calls));
        }

        [Fact]
        public async Task NativeRouteAnswersFromTheNativeSource()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");
            FakeSource native = new(MigrationRoute.Native, "new");

            var result = await Router([legacy, native], ("Migration:Routes:Types", "Native"))
                .RunAsync<IFakeSource, string>(MigrationDomain.Types, null, t => t.GetAsync());

            Assert.Equal("new", result.Data);
            Assert.Equal((0, 1), (legacy.Calls, native.Calls));
        }

        [Fact]
        public async Task NativeRouteWithoutANativeSourceFallsBackToLegacy()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");

            var result = await Router([legacy], ("Migration:Routes:Types", "Native"))
                .RunAsync<IFakeSource, string>(MigrationDomain.Types, null, t => t.GetAsync());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("old", result.Data);
        }

        [Fact]
        public async Task TheOnlySourceLeftAnswersWhateverTheRoute()
        {
            FakeSource native = new(MigrationRoute.Native, "new");

            var result = await Router([native]).RunAsync<IFakeSource, string>(MigrationDomain.Types, null, t => t.GetAsync());

            Assert.Equal("new", result.Data);
        }

        [Fact]
        public async Task NoSourceIsAFailureNotAnException()
        {
            var result = await Router([]).RunAsync<IFakeSource, string>(MigrationDomain.Types, null, t => t.GetAsync());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
        }

        [Theory]
        [InlineData("same", "same", 1, 0)]
        [InlineData("old", "new", 0, 1)]
        public async Task ShadowRouteReturnsTheLegacyAnswerAndCountsTheComparison(string legacyAnswer, string nativeAnswer, long matched, long mismatched)
        {
            // Counters are per process, so each case reports under its own feature.
            var feature = $"Shadow{legacyAnswer}{nativeAnswer}";
            FakeSource legacy = new(MigrationRoute.Legacy, legacyAnswer);
            FakeSource native = new(MigrationRoute.Native, nativeAnswer);
            var router = Router([legacy, native], ("Migration:Routes:Types", "Shadow"));

            var result = await router.RunAsync<IFakeSource, string>(MigrationDomain.Types, feature, t => t.GetAsync());

            Assert.Equal(legacyAnswer, result.Data);
            var statistics = await WaitForAsync(router, $"Types.{feature}");
            Assert.Equal((matched, mismatched, 0L), (statistics.Matched, statistics.Mismatched, statistics.Failed));
            Assert.Equal((1, 1), (legacy.Calls, native.Calls));
        }

        [Fact]
        public async Task OnlyTheShadowCallKnowsItIsOne()
        {
            // A native source skips its side effects (a paper's view count) only inside the comparison's call.
            System.Collections.Concurrent.ConcurrentDictionary<string, bool> seen = new(StringComparer.Ordinal);
            var router = Router([new FakeSource(MigrationRoute.Legacy, "same"), new FakeSource(MigrationRoute.Native, "same")], ("Migration:Routes:Types", "Shadow"));

            _ = await router.RunAsync<IFakeSource, string>(MigrationDomain.Types, "ShadowFlag", t =>
            {
                seen[t.ProviderType.Name] = MigrationRouter.InShadowCall;
                return t.GetAsync();
            });

            _ = await WaitForAsync(router, "Types.ShadowFlag");
            Assert.False(seen[MigrationRoute.Legacy.Name]);
            Assert.True(seen[MigrationRoute.Native.Name]);
            Assert.False(MigrationRouter.InShadowCall);
        }

        [Fact]
        public async Task ShadowRouteAtZeroPercentComparesNothing()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");
            FakeSource native = new(MigrationRoute.Native, "new");
            var router = Router([legacy, native], ("Migration:Routes:Types", "Shadow"), ("Migration:ShadowSamplePercent", "0"));

            _ = await router.RunAsync<IFakeSource, string>(MigrationDomain.Types, "NotSampled", t => t.GetAsync());

            Assert.Equal(0, native.Calls);
            Assert.DoesNotContain(router.GetShadowStatistics(), t => t.Key == "Types.NotSampled");
        }

        [Fact]
        public async Task WritesFollowTheirOwnRouteAndAreNeverCompared()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");
            FakeSource native = new(MigrationRoute.Native, "new");
            var router = Router([legacy, native], ("Migration:Routes:Exams", "Shadow"), ("Migration:WriteRoutes:Exams", "Shadow"));

            // Reads are shadowed; a write with the same setting stays on gama-api and is not repeated on the native source.
            Assert.Equal(MigrationRoute.Legacy, router.GetWriteRoute(MigrationDomain.Exams));
            var result = await router.RunWriteAsync<IFakeSource, string>(MigrationDomain.Exams, "WriteNotCompared", t => t.GetAsync());

            Assert.Equal("old", result.Data);
            Assert.Equal((1, 0), (legacy.Calls, native.Calls));
            Assert.DoesNotContain(router.GetShadowStatistics(), t => t.Key == "Exams.WriteNotCompared");
        }

        [Fact]
        public async Task ReadsCanMoveBeforeWrites()
        {
            FakeSource legacy = new(MigrationRoute.Legacy, "old");
            FakeSource native = new(MigrationRoute.Native, "new");
            var router = Router([legacy, native], ("Migration:Routes:Exams", "Native"));

            var read = await router.RunAsync<IFakeSource, string>(MigrationDomain.Exams, null, t => t.GetAsync());
            var write = await router.RunWriteAsync<IFakeSource, string>(MigrationDomain.Exams, null, t => t.GetAsync());

            Assert.Equal(("new", "old"), (read.Data, write.Data));

            var switched = Router([legacy, native], ("Migration:WriteRoutes:Exams", "Native"));
            Assert.Equal("new", (await switched.RunWriteAsync<IFakeSource, string>(MigrationDomain.Exams, null, t => t.GetAsync())).Data);
        }

        [Fact]
        public void ComparisonIgnoresPropertyOrderButNotListOrder()
        {
            static ResultData<JsonNode> Answer(string json) => new(OperationResult.Succeeded) { Data = JsonNode.Parse(json) };

            Assert.Null(MigrationShadowComparison.FirstDifference(Answer("""{"id":1,"title":"a"}"""), Answer("""{"title":"a","id":1}""")));
            Assert.Equal("$[0]: value differs", MigrationShadowComparison.FirstDifference(Answer("[1,2]"), Answer("[2,1]")));
            Assert.Equal("$: 2 items != 1 items", MigrationShadowComparison.FirstDifference(Answer("[1,2]"), Answer("[1]")));
        }

        [Fact]
        public void ComparisonReportsWhereNotWhat()
        {
            static ResultData<JsonNode> Answer(string json) => new(OperationResult.Succeeded) { Data = JsonNode.Parse(json) };

            var difference = MigrationShadowComparison.FirstDifference(
                Answer("""{"list":[{"email":"first@example.com"}]}"""), Answer("""{"list":[{"email":"second@example.com"}]}"""));

            Assert.Equal("$.list[0].email: value differs", difference);
            Assert.Equal(
                "$.name: only in the native answer",
                MigrationShadowComparison.FirstDifference(Answer("{}"), Answer("""{"name":"x"}""")));
            Assert.Equal(
                "operationResult: Succeeded != NotFound",
                MigrationShadowComparison.FirstDifference(Answer("{}"), new ResultData<JsonNode>(OperationResult.NotFound)));
        }

        public void Dispose() => providers.ForEach(t => t.Dispose());

        private static async Task<Data.Dto.Migration.MigrationShadowStatisticsDto> WaitForAsync(MigrationRouter router, string key)
        {
            for (var i = 0; i < 200; i++)
            {
                var statistics = router.GetShadowStatistics().FirstOrDefault(t => t.Key == key);
                if (statistics is not null && statistics.Matched + statistics.Mismatched + statistics.Failed > 0)
                {
                    return statistics;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException($"The shadow comparison of {key} did not finish.");
        }

        private MigrationRouter Router(FakeSource[] sources, params (string Key, string Value)[] settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(t => new KeyValuePair<string, string?>(t.Key, t.Value)))
                .Build();

            ServiceCollection services = new();
            _ = services.AddTransient(typeof(IGenericFactory<,>), typeof(GenericFactory<,>));
            foreach (var source in sources)
            {
                _ = services.AddSingleton<IFakeSource>(source);
            }

            var provider = services.BuildServiceProvider();
            providers.Add(provider);

            return new MigrationRouter(
                new(() => configuration),
                new(() => provider),
                new(provider.GetRequiredService<IServiceScopeFactory>),
                new(() => NullLogger<MigrationRouter>.Instance));
        }

        private sealed class FakeSource(MigrationRoute route, string answer) : IFakeSource
        {
            private int calls;

            public int Calls => Volatile.Read(ref calls);

            public MigrationRoute ProviderType => route;

            public Task<ResultData<string>> GetAsync()
            {
                _ = Interlocked.Increment(ref calls);
                return Task.FromResult(new ResultData<string>(OperationResult.Succeeded) { Data = answer });
            }
        }
    }
}
