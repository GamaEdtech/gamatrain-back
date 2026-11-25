namespace GamaEdtech.Test.Application
{
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Domain.Enumeration;
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

        // Which source each call reached: gama-api's search list or typesstats, or the routed paper list.
        private readonly List<string> sources = [];

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
        public async Task HomeStatsCombinesPaperListTotalsGamaApiAndSiteCounts()
        {
            cache["Search:v1:siteCounts"] = new JsonObject { ["teachers"] = 854, ["schools"] = 593544 };
            // The paper list's total counts papers and study materials (a number natively, a string from gama-api), and
            // gama-api's typesstats still gives exams and tutorials, in an older shape that names them azmoon and dars.
            var service = Service(HomeCounts);

            var result = await service.GetHomeStatsAsync("12", null);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("""{"paper":25813,"studyMaterials":1963,"exam":1291,"tutorial":729,"teacher":854,"school":593544}""",
                result.Data?.ToJsonString());
            Assert.Equal(["is_paper=0&perpage=1&section=12", "is_paper=1&perpage=1&section=12", "is_paper=1&section=12"], coreQueries.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task HomeStatsWithAMissingPartIsFailedSoItIsNotCachedByCloudflare()
        {
            // The database throws in this test, so the site counts are missing.
            var service = Service(_ => new(OperationResult.Succeeded) { Data = new JsonObject { ["num"] = "5" } });

            var result = await service.GetHomeStatsAsync("12", "3");

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal("""{"paper":5,"studyMaterials":5}""", result.Data?.ToJsonString());
        }

        private static ResultData<JsonObject> HomeCounts(string query) => new(OperationResult.Succeeded)
        {
            Data = query switch
            {
                "is_paper=1&perpage=1&section=12" => new JsonObject { ["num"] = 25813 },
                "is_paper=0&perpage=1&section=12" => new JsonObject { ["num"] = "1963" },
                _ => new JsonObject { ["papers"] = "1", ["exams"] = null, ["azmoon"] = "1291", ["tutorials"] = "729" },
            },
        };

        [Fact]
        public async Task WhilePapersAreOnGamaApiTheSearchIsGamaApisAsItIs()
        {
            var service = Service(_ => Succeeded());

            _ = await service.SearchAsync(Query(("type", "test"), ("section", "6627")), typesStats: false);
            _ = await service.SearchAsync(Query(("section", "6627"), ("is_paper", "1")), typesStats: true);

            Assert.Equal(["search:section=6627&type=test", "stats:is_paper=1&section=6627"], sources);
        }

        [Fact]
        public async Task OnceRoutedThePaperTabIsThePaperListWithItsTotalInTheTypeCounts()
        {
            var service = Service(query => query.Contains("type=test", StringComparison.Ordinal)
                ? new(OperationResult.Succeeded) { Data = new JsonObject { ["papers"] = false, ["test"] = false, ["exams"] = "1338", ["tutorials"] = "729" } }
                : Succeeded(), route: MigrationRoute.Native);

            var result = await service.SearchAsync(Query(("type", "test"), ("is_paper", "0"), ("section", "6627"), ("page", "2"), ("perpage", "10")), typesStats: false);

            // The list from the paper list with the search's paper filters; gama-api only for the other types' counts (it leaves
            // the searched type's own count out, and the total fills it in, as gama-api's search does).
            Assert.Equal(["papers:is_paper=0&page=2&perpage=10&section=6627", "stats:is_paper=0&page=2&perpage=10&section=6627&type=test"], sources);
            Assert.Equal("""{"num":1,"list":[{"id":"44916"}],"types_stats":{"papers":"1","test":false,"exams":"1338","tutorials":"729"}}""", result.Data?.ToJsonString());
        }

        [Fact]
        public async Task OnceRoutedEveryPapersCountIsThePaperListsTotal()
        {
            var service = Service(CountsOfOtherTypes, route: MigrationRoute.Native);

            var stats = await service.SearchAsync(Query(("section", "6627"), ("is_paper", "1"), ("title", "bio")), typesStats: true);
            var exams = await service.SearchAsync(Query(("type", "azmoon"), ("section", "6627")), typesStats: false);

            Assert.Equal("""{"papers":"42","test":"42","exams":"1338"}""", stats.Data?.ToJsonString());
            Assert.Equal(["stats:is_paper=1&section=6627&title=bio", "papers:is_paper=1&perpage=1&section=6627&title=bio", "search:section=6627&type=azmoon", "papers:perpage=1&section=6627"], sources);
            // Another type's own answer is gama-api's; only its papers count in types_stats is ours.
            Assert.Equal("""{"num":"5","list":[],"types_stats":{"papers":"42","exams":"5"}}""", exams.Data?.ToJsonString());
        }

        [Fact]
        public async Task EachKindOfRelatedRowComesFromItsOwnSource()
        {
            var service = Service(_ => throw new InvalidOperationException("gama-api not expected"), route: MigrationRoute.Native);

            var paperPage = await service.GetRelatedAsync(Query(("source", "papers"), ("id", "45757"), ("request", "test,file,exam"), ("zzz", "1")));
            var tutorialPage = await service.GetRelatedAsync(Query(("source", "tutorial"), ("id", "1862")));

            // Only the kinds asked for are fetched; multimedia (file) is gama-api's only, so it is always empty.
            Assert.Equal("""{"tests":[{"id":"45758"}],"papers":[{"id":"45758"}],"files":[],"questions":[],"exams":[{"id":"2527"}],"tutorials":[],"blogs":[]}""", paperPage.Data?.ToJsonString());
            Assert.Equal("""{"tests":[{"id":"45758"}],"papers":[{"id":"45758"}],"files":[],"questions":[],"exams":[{"id":"2527"}],"tutorials":[{"id":"1863"}],"blogs":[]}""", tutorialPage.Data?.ToJsonString());
            Assert.Equal(["relatedPapers:test:45757", "relatedExams:test:45757", "relatedPapers:tutorial:1862", "relatedExams:tutorial:1862", "relatedTutorials:tutorial:1862"], sources);
        }

        [Fact]
        public async Task AMultimediaPagesRelatedRowsStayGamaApis()
        {
            var service = Service(_ => Succeeded());

            _ = await service.GetRelatedAsync(Query(("source", "file"), ("id", "88")));

            Assert.Equal(["related:id=88&source=file"], sources);
        }

        [Fact]
        public async Task OnceTutorialsAreRoutedTheTutorialTabIsTheTutorialListWithItsTotalInTheTypeCounts()
        {
            var service = Service(_ => new(OperationResult.Succeeded) { Data = new JsonObject { ["papers"] = "33086", ["exams"] = "1338", ["tutorials"] = "729", ["dars"] = "729" } },
                tutorialRoute: MigrationRoute.Native, tutorials: TutorialList);

            var result = await service.SearchAsync(Query(("type", "tutorial"), ("lesson", "6529"), ("page", "2"), ("zzz", "1")), typesStats: false);

            // Only the filters the tutorials search knows reach it; gama-api still gives the other types' counts, and the
            // tutorials count (here, of the whole filtered list) is ours. Papers are still gama-api's.
            Assert.Equal(["tutorials:lesson=6529&page=2", "stats:lesson=6529&page=2&type=tutorial", "tutorials:lesson=6529&perpage=1"], sources);
            Assert.Equal("""{"num":3,"list":[{"id":"1862"}],"types_stats":{"papers":"33086","exams":"1338","tutorials":"3","dars":"3"}}""", result.Data?.ToJsonString());

            // The website's noTypesStats leaves the counts out.
            sources.Clear();
            var plain = await service.SearchAsync(Query(("type", "dars"), ("topic", "10543"), ("noTypesStats", "1")), typesStats: false);
            Assert.Equal(["tutorials:topic=10543"], sources);
            Assert.Equal("""{"num":3,"list":[{"id":"1862"}]}""", plain.Data?.ToJsonString());
        }

        [Fact]
        public async Task WhileTutorialsAreOnGamaApiTheirTabAndPageAreGamaApis()
        {
            var service = Service(_ => Succeeded(), route: MigrationRoute.Native, tutorials: TutorialList);

            _ = await service.SearchAsync(Query(("type", "tutorial"), ("lesson", "6529")), typesStats: false);

            // Another type's search: gama-api's, with only the papers count ours.
            Assert.DoesNotContain(sources, t => t.StartsWith("tutorials:", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ATutorialsPageIsCachedPerTutorialAndAFailureIsNot()
        {
            var service = Service(_ => throw new InvalidOperationException("not used"), tutorialRoute: MigrationRoute.Native,
                tutorials: query => query == "id=404" ? new(OperationResult.Failed) : new(OperationResult.Succeeded) { Data = new JsonObject { ["id"] = query[3..] } });

            _ = await service.GetTutorialAsync(1862);
            var again = await service.GetTutorialAsync(1862);
            _ = await service.GetTutorialAsync(404);
            _ = await service.GetTutorialAsync(404);

            Assert.Equal("""{"id":"1862"}""", again.Data?.ToJsonString());
            Assert.Equal(["tutorial:1862", "tutorial:404", "tutorial:404"], sources);
        }

        [Fact]
        public async Task OnceExamsAreRoutedTheExamTabAndItsCountsAreOurs()
        {
            var service = Service(_ => new(OperationResult.Succeeded) { Data = new JsonObject { ["papers"] = "33086", ["exams"] = "1610", ["azmoon"] = "1610", ["tutorials"] = "729" } },
                examRoute: MigrationRoute.Native);

            var result = await service.SearchAsync(Query(("type", "azmoon"), ("lesson", "6346"), ("exam_type", "6042")), typesStats: false);

            Assert.Equal(["exams:exam_type=6042&lesson=6346", "stats:exam_type=6042&lesson=6346&type=azmoon", "exams:exam_type=6042&lesson=6346&perpage=1"], sources);
            Assert.Equal("""{"num":"2","list":[{"id":"2540"}],"types_stats":{"papers":"33086","exams":"2","azmoon":"2","tutorials":"729"}}""", result.Data?.ToJsonString());
        }

        private static ResultData<JsonObject> TutorialList(string query) => new(OperationResult.Succeeded)
        {
            Data = new JsonObject { ["num"] = 3, ["list"] = new JsonArray(new JsonObject { ["id"] = "1862" }) },
        };

        private static ResultData<JsonObject> CountsOfOtherTypes(string query) => new(OperationResult.Succeeded)
        {
            Data = query switch
            {
                _ when query.Contains("perpage=1", StringComparison.Ordinal) => new JsonObject { ["num"] = 42 },
                _ when query.Contains("type=azmoon", StringComparison.Ordinal) => new JsonObject
                {
                    ["num"] = "5",
                    ["list"] = new JsonArray(),
                    ["types_stats"] = new JsonObject { ["papers"] = "33086", ["exams"] = "5" },
                },
                _ => new JsonObject { ["papers"] = "33086", ["test"] = "33086", ["exams"] = "1338" },
            },
        };

        private static ResultData<JsonObject> Succeeded() => new(OperationResult.Succeeded)
        {
            Data = new JsonObject { ["list"] = new JsonArray(new JsonObject { ["id"] = "44916" }), ["num"] = 1 },
        };

        private static Dictionary<string, StringValues> Query(params (string Key, string Value)[] items)
            => items.ToDictionary(t => t.Key, t => new StringValues(t.Value));

        private SearchService Service(Func<string, ResultData<JsonObject>> gamaApi, Func<long, ResultData<JsonObject>>? paper = null, MigrationRoute? route = null,
            MigrationRoute? tutorialRoute = null, Func<string, ResultData<JsonObject>>? tutorials = null, MigrationRoute? examRoute = null) => new(
            new(() => throw new InvalidOperationException("database touched")),
            new(() => new HttpContextAccessor()),
            new(() => null!),
            new(() => NullLogger<SearchService>.Instance),
            new(() => Fake.Create<ICoreProvider>(new()
            {
                ["GetRelatedAsync"] = args =>
                {
                    sources.Add($"related:{args[0]}");
                    return Task.FromResult(gamaApi((string)args[0]!));
                },
                ["SearchAsync"] = args =>
                {
                    coreQueries.Add((string)args[0]!);
                    sources.Add($"{((bool)args[1]! ? "stats" : "search")}:{args[0]}");
                    return Task.FromResult(gamaApi((string)args[0]!));
                },
            })),
            new(() => new PassThroughMigrationRouter(Fake.Create<IPastPaperSource>(new()
            {
                ["SearchPapersAsync"] = args =>
                {
                    coreQueries.Add((string)args[0]!);
                    sources.Add($"papers:{args[0]}");
                    return Task.FromResult(gamaApi((string)args[0]!));
                },
                ["GetPaperAsync"] = args => Task.FromResult((paper ?? throw new InvalidOperationException("paper not expected"))((long)args[0]!)),
                ["GetRelatedPapersAsync"] = args =>
                {
                    sources.Add($"relatedPapers:{args[0]}:{args[1]}");
                    return Task.FromResult(new ResultData<JsonArray>(OperationResult.Succeeded) { Data = [new JsonObject { ["id"] = "45758" }] });
                },
            }), Fake.Create<ITutorialSource>(new()
            {
                ["SearchTutorialsAsync"] = args =>
                {
                    sources.Add($"tutorials:{args[0]}");
                    return Task.FromResult((tutorials ?? throw new InvalidOperationException("tutorials not expected"))((string)args[0]!));
                },
                ["GetTutorialAsync"] = args =>
                {
                    sources.Add($"tutorial:{args[0]}");
                    return Task.FromResult((tutorials ?? throw new InvalidOperationException("tutorials not expected"))($"id={args[0]}"));
                },
                ["GetRelatedTutorialsAsync"] = args =>
                {
                    sources.Add($"relatedTutorials:{args[0]}:{args[1]}");
                    return Task.FromResult(new ResultData<JsonArray>(OperationResult.Succeeded) { Data = [new JsonObject { ["id"] = "1863" }] });
                },
            }), Fake.Create<IExamSource>(new()
            {
                ["SearchExamsAsync"] = args =>
                {
                    sources.Add($"exams:{args[0]}");
                    return Task.FromResult(new ResultData<JsonObject>(OperationResult.Succeeded) { Data = new JsonObject { ["num"] = "2", ["list"] = new JsonArray(new JsonObject { ["id"] = "2540" }) } });
                },
                ["GetRelatedExamsAsync"] = args =>
                {
                    sources.Add($"relatedExams:{args[0]}:{args[1]}");
                    return Task.FromResult(new ResultData<JsonArray>(OperationResult.Succeeded) { Data = [new JsonObject { ["id"] = "2527" }] });
                },
            }))
            {
                Routes = new Dictionary<string, MigrationRoute>(StringComparer.Ordinal)
                {
                    [MigrationDomain.Exams.Name] = examRoute ?? MigrationRoute.Legacy,
                    [MigrationDomain.PastPapers.Name] = route ?? MigrationRoute.Legacy,
                    [MigrationDomain.Tutorials.Name] = tutorialRoute ?? MigrationRoute.Legacy,
                },
            }),
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
