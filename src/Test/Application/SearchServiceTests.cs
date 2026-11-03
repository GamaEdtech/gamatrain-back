namespace GamaEdtech.Test.Application
{
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Primitives;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;

    /// <summary>
    /// The subject directory's paper list is proxied and cached for everyone, so only the parameters the page sends may
    /// reach gama-api or the cache key, the same search must always hit the same entry, and a failure is never cached.
    /// </summary>
    public class SearchServiceTests
    {
        private readonly Dictionary<string, object?> cache = [];
        private readonly List<string> coreQueries = [];

        [Fact]
        public async Task PaperListForwardsOnlyTheDirectoryParametersSorted()
        {
            var service = Service(_ => Succeeded());

            _ = await service.SearchPapersAsync(Query(
                ("perpage", "50"), ("zzz", "1"), ("lesson", "2204"), ("page", "1"), ("directory", "true"), ("is_paper", "true"),
                ("edu_year", " "), ("title", "not for this list")));

            Assert.Equal(["directory=true&is_paper=true&lesson=2204&page=1&perpage=50"], coreQueries);
        }

        [Fact]
        public async Task PaperListIsServedFromTheCacheTheSecondTime()
        {
            var service = Service(_ => Succeeded());

            var first = await service.SearchPapersAsync(Query(("lesson", "2204"), ("page", "1")));
            // The same search with its parameters in another order is the same cache entry.
            var second = await service.SearchPapersAsync(Query(("page", "1"), ("lesson", "2204")));

            _ = Assert.Single(coreQueries);
            Assert.Equal(OperationResult.Succeeded, second.OperationResult);
            Assert.Equal(first.Data?.ToJsonString(), second.Data?.ToJsonString());
        }

        [Fact]
        public async Task FailedPaperListIsNotCached()
        {
            var service = Service(_ => new ResultData<JsonObject>(OperationResult.Failed) { Errors = [new() { Message = "gama-api is down" }] });

            var result = await service.SearchPapersAsync(Query(("lesson", "2204")));
            _ = await service.SearchPapersAsync(Query(("lesson", "2204")));

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Empty(cache);
            Assert.Equal(2, coreQueries.Count);
        }

        [Fact]
        public async Task PaperListAndSiteSearchDoNotShareCacheEntries()
        {
            var service = Service(_ => Succeeded());

            _ = await service.SearchPapersAsync(Query(("lesson", "2204")));
            _ = await service.SearchAsync(Query(("lesson", "2204")), typesStats: false);

            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public async Task PaperDetailsAreCachedPerPaperAndFailuresAreNot()
        {
            var calls = 0;
            var service = Service(_ => throw new InvalidOperationException("not used"), id =>
            {
                calls++;
                return id == 404
                    ? new(OperationResult.Failed) { Errors = [new() { Message = "permissionDenied" }] }
                    : Succeeded();
            });

            _ = await service.GetPaperAsync(43269);
            var again = await service.GetPaperAsync(43269);
            _ = await service.GetPaperAsync(44916);
            var hidden = await service.GetPaperAsync(404);
            _ = await service.GetPaperAsync(404);

            Assert.Equal(OperationResult.Succeeded, again.OperationResult);
            Assert.Equal(OperationResult.Failed, hidden.OperationResult);
            // 43269 once, 44916 once, the failing paper every time.
            Assert.Equal(4, calls);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public async Task HomeStatsCombinesGamaApiAndSiteCounts()
        {
            cache["Search:v1:siteCounts"] = new JsonObject { ["teachers"] = 854, ["schools"] = 593544 };
            var service = Service(query => new(OperationResult.Succeeded)
            {
                // gama-api sends counts as strings, and an older shape uses azmoon/dars for exams/tutorials.
                Data = query.Contains("is_paper=1", StringComparison.Ordinal)
                    ? new JsonObject { ["papers"] = "25813", ["exams"] = null, ["azmoon"] = "1291", ["tutorials"] = "729" }
                    : new JsonObject { ["papers"] = "1963" },
            });

            var result = await service.GetHomeStatsAsync("12", null);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("""{"paper":25813,"studyMaterials":1963,"exam":1291,"tutorial":729,"teacher":854,"school":593544}""",
                result.Data?.ToJsonString());
            Assert.Equal(["is_paper=1&section=12", "is_paper=0&section=12"], coreQueries);
        }

        [Fact]
        public async Task HomeStatsWithAMissingPartIsFailedSoItIsNotCachedByCloudflare()
        {
            // The database throws in this test, so the site counts are missing.
            var service = Service(_ => new(OperationResult.Succeeded) { Data = new JsonObject { ["papers"] = "5" } });

            var result = await service.GetHomeStatsAsync("12", "3");

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal("""{"paper":5,"studyMaterials":5}""", result.Data?.ToJsonString());
        }

        private static ResultData<JsonObject> Succeeded() => new(OperationResult.Succeeded)
        {
            Data = new JsonObject { ["list"] = new JsonArray(new JsonObject { ["id"] = "44916" }), ["num"] = 1 },
        };

        private static Dictionary<string, StringValues> Query(params (string Key, string Value)[] items)
            => items.ToDictionary(t => t.Key, t => new StringValues(t.Value));

        private SearchService Service(Func<string, ResultData<JsonObject>> gamaApi, Func<long, ResultData<JsonObject>>? paper = null) => new(
            new(() => throw new InvalidOperationException("database touched")),
            new(() => new HttpContextAccessor()),
            new(() => null!),
            new(() => NullLogger<SearchService>.Instance),
            new(() => Fake.Create<ICoreProvider>(new()
            {
                ["SearchPapersAsync"] = args =>
                {
                    coreQueries.Add((string)args[0]!);
                    return Task.FromResult(gamaApi((string)args[0]!));
                },
                ["SearchAsync"] = args =>
                {
                    coreQueries.Add((string)args[0]!);
                    return Task.FromResult(gamaApi((string)args[0]!));
                },
                ["GetPaperAsync"] = args => Task.FromResult((paper ?? throw new InvalidOperationException("paper not expected"))((long)args[0]!)),
            })),
            new(() => Fake.Create<ICacheProvider>(new()
            {
                ["GetAsync"] = args => Task.FromResult(cache.TryGetValue((string)args[0]!, out var value) ? value as JsonObject : null),
                ["SetAsync"] = args =>
                {
                    cache[(string)args[0]!] = args[1];
                    return Task.CompletedTask;
                },
            })));
    }
}
