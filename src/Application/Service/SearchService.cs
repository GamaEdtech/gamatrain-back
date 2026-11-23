namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Frozen;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;

    using static GamaEdtech.Common.Core.Constants;

    public class SearchService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<SearchService>> localizer, Lazy<ILogger<SearchService>> logger, Lazy<ICoreProvider> coreProvider, Lazy<IMigrationRouter> migrationRouter,
        Lazy<ICacheProvider> cacheProvider)
        : LocalizableServiceBase<SearchService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), Interface.ISearchService
    {
        // What the frontend sends (buildSearchParams and the service counts). Anything else is dropped, so random
        // parameters can't create new cache entries.
        private static readonly FrozenSet<string> QueryKeys = new[]
        {
            "page", "perpage", "noTypesStats", "title", "type", "is_paper", "section", "base", "lesson", "topic",
            "test_type", "variant", "exam_type", "content_type", "edu_year", "edu_month",
        }.ToFrozenSet(StringComparer.Ordinal);

        // What the subject directory sends to tests/search (its paper list and its other resources).
        // A paper count is the paper list's total for a board, grade and kind (gama-api's typesstats counts the same search).
        private static readonly FrozenSet<string> PaperCountKeys = new[] { "section", "base", "is_paper", "perpage" }.ToFrozenSet(StringComparer.Ordinal);

        // The search's filters that the paper list (tests/search) understands: gama-api's search runs the same paper search
        // for them, so its paper results and counts are the paper list's.
        private static readonly FrozenSet<string> PaperSearchKeys = new[]
        {
            "page", "perpage", "title", "is_paper", "section", "base", "lesson", "topic", "test_type", "variant", "edu_year", "edu_month",
        }.ToFrozenSet(StringComparer.Ordinal);

        // What a page's "related" rows send: the item (source type and id) and the types wanted.
        private static readonly FrozenSet<string> RelatedKeys = new[] { "source", "id", "request" }.ToFrozenSet(StringComparer.Ordinal);

        // What the website sends to types/list for the lists this backend copies.
        private static readonly FrozenSet<string> TypeKeys = new[] { "type", "section_id", "base_id", "course_id", "lesson_id", "is_paper", "is_school" }
            .ToFrozenSet(StringComparer.Ordinal);

        // gama-api's search types that are past papers.
        private static readonly FrozenSet<string> PaperTypes = new[] { "test", "paper", "papers" }.ToFrozenSet(StringComparer.Ordinal);

        // gama-api's search types that are tutorials.
        private static readonly FrozenSet<string> TutorialTypes = new[] { "tutorial", "tutorials", "dars" }.ToFrozenSet(StringComparer.Ordinal);

        // The random practice question's filters.
        private static readonly FrozenSet<string> RandomQuestionKeys = new[] { "lesson", "topic" }.ToFrozenSet(StringComparer.Ordinal);

        // gama-api's search types that are exams.
        private static readonly FrozenSet<string> ExamTypes = new[] { "azmoon", "exam", "exams" }.ToFrozenSet(StringComparer.Ordinal);

        // The search's filters the exams search understands.
        private static readonly FrozenSet<string> ExamSearchKeys = new[]
        {
            "page", "perpage", "title", "section", "base", "course", "lesson", "topic", "exam_type", "azmoon_type", "examOpen", "level",
            "date_status", "test_id",
        }.ToFrozenSet(StringComparer.Ordinal);

        // The search's filters the tutorials search understands.
        private static readonly FrozenSet<string> TutorialSearchKeys = new[] { "page", "perpage", "title", "section", "base", "lesson", "topic" }
            .ToFrozenSet(StringComparer.Ordinal);

        private static readonly FrozenSet<string> PaperQueryKeys = new[]
        {
            "page", "perpage", "lesson", "is_paper", "directory", "edu_year", "edu_month", "test_type", "variant", "facet",
        }.ToFrozenSet(StringComparer.Ordinal);

        private const int MaxValueLength = 200;

        // A new paper shows up within 5 minutes; the API answer is also cached by Cloudflare (see SearchController).
        private static readonly DistributedCacheEntryOptions CacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

        public Task<ResultData<JsonObject>> SearchAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query, bool typesStats)
        {
            var queryString = NormalizeQuery(query, QueryKeys);
            // Versioned: bump it if what is cached changes shape.
            var cacheKey = $"Search:v1:{(typesStats ? "stats" : "list")}:{queryString}";

            // gama-api answers it all while past papers and tutorials are on gama-api. Once a route moves, that type in it (its
            // list and every count of it) follows the route; everything else stays gama-api's.
            var papersMoved = migrationRouter.Value.GetRoute(MigrationDomain.PastPapers, MigrationFeatures.Website) != MigrationRoute.Legacy;
            var tutorialsMoved = migrationRouter.Value.GetRoute(MigrationDomain.Tutorials, MigrationFeatures.Website) != MigrationRoute.Legacy;
            var examsMoved = migrationRouter.Value.GetRoute(MigrationDomain.Exams, MigrationFeatures.Website) != MigrationRoute.Legacy;
            if (!papersMoved && !tutorialsMoved && !examsMoved)
            {
                return GetOrFetchAsync(cacheKey, () => coreProvider.Value.SearchAsync(queryString, typesStats));
            }

            var parsed = QueryHelpers.ParseQuery(queryString);
            var type = parsed.GetValueOrDefault("type").ToString();
            return GetOrFetchAsync(cacheKey, typesStats switch
            {
                true => () => TypesStatsAsync(queryString, parsed),
                false when papersMoved && PaperTypes.Contains(type) => () => PaperSearchAsync(parsed),
                false when tutorialsMoved && TutorialTypes.Contains(type) => () => MovedTypeSearchAsync(parsed, Tutorials),
                false when examsMoved && ExamTypes.Contains(type) => () => MovedTypeSearchAsync(parsed, Exams),
                _ => () => OtherSearchAsync(queryString, parsed),
            });
        }

        public Task<ResultData<JsonObject>> GetRelatedAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query)
        {
            var queryString = NormalizeQuery(query, RelatedKeys);
            var cacheKey = $"Search:v1:related:{queryString}";
            var parsed = QueryHelpers.ParseQuery(queryString);

            // The rows of a multimedia or Q&A page stay gama-api's, as those pages are.
            if (RelatedContent.Kind(parsed.GetValueOrDefault("source").ToString()) is not { } source
                || !long.TryParse(parsed.GetValueOrDefault("id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return GetOrFetchAsync(cacheKey, () => coreProvider.Value.GetRelatedAsync(queryString));
            }

            // No request means every kind, as on gama-api.
            var request = parsed.GetValueOrDefault("request").ToString();
            HashSet<string> wanted = request.Length == 0
                ? [RelatedContent.PaperKind, RelatedContent.ExamKind, RelatedContent.TutorialKind]
                : [.. request.Split(',').Select(RelatedContent.Kind).OfType<string>()];
            return GetOrFetchAsync(cacheKey, () => RelatedRowsAsync(source, id, wanted));
        }

        /// <summary>
        /// A paper, exam or tutorial page's related rows, each kind from its own source on its route (gama-api's answer shape:
        /// <c>tests</c> and its alias <c>papers</c>, <c>exams</c>, <c>tutorials</c>). The kinds only gama-api has (<c>files</c>,
        /// <c>questions</c>, <c>blogs</c>) are always empty: multimedia, Q&amp;A and blogs go away with it. One failed kind fails
        /// the answer, so a partial one is never cached.
        /// </summary>
        private async Task<ResultData<JsonObject>> RelatedRowsAsync(string source, long id, HashSet<string> wanted)
        {
            var router = migrationRouter.Value;
            var papers = wanted.Contains(RelatedContent.PaperKind)
                ? await router.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource s) => s.GetRelatedPapersAsync(source, id))
                : NoRows();
            var exams = wanted.Contains(RelatedContent.ExamKind)
                ? await router.RunAsync(MigrationDomain.Exams, MigrationFeatures.Website, (IExamSource s) => s.GetRelatedExamsAsync(source, id))
                : NoRows();
            var tutorials = wanted.Contains(RelatedContent.TutorialKind)
                ? await router.RunAsync(MigrationDomain.Tutorials, MigrationFeatures.Website, (ITutorialSource s) => s.GetRelatedTutorialsAsync(source, id))
                : NoRows();
            return papers.Data is null || exams.Data is null || tutorials.Data is null
                ? new(OperationResult.Failed) { Errors = papers.Errors ?? exams.Errors ?? tutorials.Errors }
                : new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ["tests"] = papers.Data.DeepClone(),
                        ["papers"] = papers.Data.DeepClone(),
                        ["files"] = new JsonArray(),
                        ["questions"] = new JsonArray(),
                        ["exams"] = exams.Data.DeepClone(),
                        ["tutorials"] = tutorials.Data.DeepClone(),
                        ["blogs"] = new JsonArray(),
                    },
                };

            static ResultData<JsonArray> NoRows() => new(OperationResult.Succeeded) { Data = [] };
        }

        public async Task<ResultData<JsonArray>> GetTypesAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query)
        {
            var queryString = NormalizeQuery(query, TypeKeys);
            var type = QueryHelpers.ParseQuery(queryString).GetValueOrDefault("type").ToString();
            if (!LegacyTaxonomyKinds.ByListName.ContainsKey(type))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = $"Unknown list {type}." }] };
            }

            // The cache keeps objects; a list is kept under "list".
            var result = await GetOrFetchAsync($"Search:v1:types:{queryString}", async () =>
            {
                var list = await migrationRouter.Value.RunAsync(MigrationDomain.Types, MigrationFeatures.Website, (ITaxonomySource source) => source.GetTypeListAsync(queryString));
                return list.Data is null
                    ? new(list.OperationResult) { Errors = list.Errors }
                    : new(list.OperationResult) { Data = new() { ["list"] = list.Data }, Errors = list.Errors };
            });
            return result.Data?["list"] is JsonArray items
                ? new(result.OperationResult) { Data = (JsonArray)items.DeepClone(), Errors = result.Errors }
                : new(result.OperationResult) { Errors = result.Errors };
        }

        public Task<ResultData<JsonObject>> SearchPapersAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query)
        {
            var queryString = NormalizeQuery(query, PaperQueryKeys);
            return GetOrFetchAsync($"Search:v1:papers:{queryString}", () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.SearchPapersAsync(queryString)));
        }

        public Task<ResultData<JsonObject>> GetPaperAsync(long id)
            => GetOrFetchAsync($"Search:v1:paper:{id}", () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.GetPaperAsync(id)));

        /// <summary>The cache keys of a tutorial's page and a subject's lesson tree, dropped when an admin changes a tutorial.</summary>
        internal static string TutorialCacheKey(long id) => $"Search:v1:tutorial:{id}";

        internal static string LessonTreeCacheKey(long subjectId) => $"Search:v1:lessonTree:{subjectId}";

        public Task<ResultData<JsonObject>> GetExamTestPageAsync([NotNull] string identity, bool full)
            => GetOrFetchAsync($"Search:v1:examTest:{identity}:{full}", () => migrationRouter.Value.RunAsync(MigrationDomain.Exams, MigrationFeatures.Website, (IExamSource source) => source.GetExamTestPageAsync(identity, full)));

        // Never cached: a cached answer would give every visitor the same question.
        public Task<ResultData<JsonObject>> GetRandomExamTestAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query)
        {
            var queryString = NormalizeQuery(query, RandomQuestionKeys);
            return migrationRouter.Value.RunAsync(MigrationDomain.Exams, MigrationFeatures.Website, (IExamSource source) => source.GetRandomExamTestAsync(queryString));
        }

        public Task<ResultData<JsonObject>> GetTutorialAsync(long id)
            => GetOrFetchAsync(TutorialCacheKey(id), () => migrationRouter.Value.RunAsync(MigrationDomain.Tutorials, MigrationFeatures.Website, (ITutorialSource source) => source.GetTutorialAsync(id)));

        public Task<ResultData<JsonObject>> GetLessonTreeAsync(long subjectId)
            => GetOrFetchAsync(LessonTreeCacheKey(subjectId), () => migrationRouter.Value.RunAsync(MigrationDomain.Tutorials, MigrationFeatures.Website, (ITutorialSource source) => source.GetLessonTreeAsync(subjectId)));

        public async Task<ResultData<JsonObject>> GetHomeStatsAsync(string? section, string? grade)
        {
            KeyValuePair<string, StringValues>[] filters = [new("section", section), new("base", grade)];
            // Each part is cached on its own: the counts per board and grade, the site counts once for all. Papers and study
            // materials are the paper list's totals, so they follow the past papers' route, which is our table once Native.
            // Exams come from gama-api's typesstats; tutorials too, with their count from the routed tutorials search once moved.
            var papersTask = PaperCountAsync(section, grade, isPaper: true);
            var studyMaterialsTask = PaperCountAsync(section, grade, isPaper: false);
            var othersTask = SearchAsync([.. filters, new("is_paper", "1")], typesStats: true);
            var siteTask = GetOrFetchAsync("Search:v1:siteCounts", GetSiteCountsAsync);
            var papers = (await papersTask).Data;
            var studyMaterials = (await studyMaterialsTask).Data;
            var others = (await othersTask).Data;
            var site = (await siteTask).Data;

            var stats = new JsonObject();
            AddCount(stats, "paper", papers, "num");
            AddCount(stats, "studyMaterials", studyMaterials, "num");
            AddCount(stats, "exam", others, "exams", "azmoon");
            AddCount(stats, "tutorial", others, "tutorials", "dars");
            AddCount(stats, "teacher", site, "teachers");
            AddCount(stats, "school", site, "schools");

            var complete = papers is not null && studyMaterials is not null && others is not null && site is not null;
            return new(complete ? OperationResult.Succeeded : OperationResult.Failed) { Data = stats };
        }

        /// <summary>
        /// gama-api's <c>search?type=test</c> from the routed paper list: its <c>num</c> and <c>list</c> are the paper list's
        /// (gama-api runs the same paper search for both), and <c>types_stats</c> (unless <c>noTypesStats</c>) is the other
        /// types' counts with <c>papers</c> set to that total, as gama-api sets it. gama-api's <c>pageInfo</c> and filter
        /// titles are not sent: the website reads neither.
        /// </summary>
        private async Task<ResultData<JsonObject>> PaperSearchAsync(Dictionary<string, StringValues> parsed)
        {
            var paperQuery = NormalizeQuery(parsed, PaperSearchKeys);
            var papers = await migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.SearchPapersAsync(paperQuery));
            if (papers.OperationResult is not OperationResult.Succeeded || papers.Data is null)
            {
                return papers;
            }

            JsonObject data = new() { ["num"] = papers.Data["num"]?.DeepClone(), ["list"] = papers.Data["list"]?.DeepClone() };
            if (string.IsNullOrEmpty(parsed.GetValueOrDefault("noTypesStats")))
            {
                var stats = await SearchAsync(parsed, typesStats: true);
                if (stats.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = stats.Errors };
                }

                var typesStats = (JsonObject)stats.Data.DeepClone();
                typesStats["papers"] = data["num"]?.ToString();
                data["types_stats"] = typesStats;
            }

            return new(OperationResult.Succeeded) { Data = data };
        }

        /// <summary>
        /// gama-api's <c>search?type=tutorial</c> from the routed tutorials search: its <c>num</c> and <c>list</c>, and
        /// <c>types_stats</c> (unless <c>noTypesStats</c>) with <c>tutorials</c> set to that total. gama-api's <c>pageInfo</c>
        /// is not sent: the website does not read it.
        /// </summary>
        private async Task<ResultData<JsonObject>> MovedTypeSearchAsync(Dictionary<string, StringValues> parsed, MovedType moved)
        {
            var typeQuery = NormalizeQuery(parsed, moved.Keys);
            var found = await moved.Search(migrationRouter.Value, typeQuery);
            if (found.OperationResult is not OperationResult.Succeeded || found.Data is null)
            {
                return found;
            }

            JsonObject data = new() { ["num"] = found.Data["num"]?.DeepClone(), ["list"] = found.Data["list"]?.DeepClone() };
            if (string.IsNullOrEmpty(parsed.GetValueOrDefault("noTypesStats")))
            {
                var stats = await SearchAsync(parsed, typesStats: true);
                if (stats.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = stats.Errors };
                }

                var typesStats = (JsonObject)stats.Data.DeepClone();
                foreach (var key in moved.CountKeys.Where(t => t == moved.CountKeys[0] || typesStats.ContainsKey(t)))
                {
                    typesStats[key] = data["num"]?.ToString();
                }

                data["types_stats"] = typesStats;
            }

            return new(OperationResult.Succeeded) { Data = data };
        }

        /// <summary>A type of the search that moves off gama-api on its own route: its filters, its routed search, and its count
        /// keys in <c>types_stats</c> (the first always set, the others only where gama-api sends them).</summary>
        private sealed record MovedType(string Name, FrozenSet<string> Keys, string[] CountKeys, Func<IMigrationRouter, string, Task<ResultData<JsonObject>>> Search);

        private static readonly MovedType Tutorials = new("tutorial", TutorialSearchKeys, ["tutorials", "dars"],
            (router, query) => router.RunAsync(MigrationDomain.Tutorials, MigrationFeatures.Website, (ITutorialSource source) => source.SearchTutorialsAsync(query)));

        private static readonly MovedType Exams = new("exam", ExamSearchKeys, ["exams", "azmoon"],
            (router, query) => router.RunAsync(MigrationDomain.Exams, MigrationFeatures.Website, (IExamSource source) => source.SearchExamsAsync(query)));

        /// <summary>A moved type's count in <c>types_stats</c> for the same filters: its routed search's total (one row asked for).</summary>
        private async Task<ResultData<bool>> ReplaceCountAsync(JsonObject stats, Dictionary<string, StringValues> parsed, MovedType moved)
        {
            Dictionary<string, StringValues> countQuery = new(parsed, StringComparer.Ordinal) { ["perpage"] = "1" };
            _ = countQuery.Remove("page");
            var typeQuery = NormalizeQuery(countQuery, moved.Keys);
            var found = await GetOrFetchAsync($"Search:v1:{moved.Name}Count:{typeQuery}", () => moved.Search(migrationRouter.Value, typeQuery));
            if (found.Data?["num"] is not JsonNode num)
            {
                return new(OperationResult.Failed) { Errors = found.Errors };
            }

            foreach (var key in moved.CountKeys.Where(t => IsCount(stats[t])))
            {
                stats[key] = num.ToString();
            }

            return new(OperationResult.Succeeded) { Data = true };
        }

        /// <summary>Another type's search (exams...): gama-api's answer, with its <c>types_stats</c> counts of the moved types
        /// taken from their routed lists.</summary>
        private async Task<ResultData<JsonObject>> OtherSearchAsync(string queryString, Dictionary<string, StringValues> parsed)
        {
            var result = await coreProvider.Value.SearchAsync(queryString, typesStats: false);
            return result.Data?["types_stats"] is JsonObject stats ? await WithOurCountsAsync(result, stats, parsed) : result;
        }

        /// <summary>gama-api's <c>typesstats</c> with the counts of the moved types taken from their routed lists for the same
        /// filters: <c>papers</c> (and the older <c>test</c> alias) once past papers move, <c>tutorials</c> (and <c>dars</c>)
        /// once tutorials move. A count gama-api leaves out (the searched type's own) stays out.</summary>
        private async Task<ResultData<JsonObject>> TypesStatsAsync(string queryString, Dictionary<string, StringValues> parsed)
        {
            var result = await coreProvider.Value.SearchAsync(queryString, typesStats: true);
            return result.Data is { } stats ? await WithOurCountsAsync(result, stats, parsed) : result;
        }

        private async Task<ResultData<JsonObject>> WithOurCountsAsync(ResultData<JsonObject> result, JsonObject stats, Dictionary<string, StringValues> parsed)
        {
            foreach (var (domain, moved) in new[] { (MigrationDomain.Tutorials, Tutorials), (MigrationDomain.Exams, Exams) })
            {
                if (migrationRouter.Value.GetRoute(domain, MigrationFeatures.Website) != MigrationRoute.Legacy && moved.CountKeys.Any(t => IsCount(stats[t])))
                {
                    var replaced = await ReplaceCountAsync(stats, parsed, moved);
                    if (replaced.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(OperationResult.Failed) { Errors = replaced.Errors };
                    }
                }
            }

            if (migrationRouter.Value.GetRoute(MigrationDomain.PastPapers, MigrationFeatures.Website) == MigrationRoute.Legacy || !IsCount(stats["papers"]))
            {
                return result;
            }

            Dictionary<string, StringValues> countQuery = new(parsed, StringComparer.Ordinal) { ["perpage"] = "1" };
            _ = countQuery.Remove("page");
            var queryString = NormalizeQuery(countQuery, PaperSearchKeys);
            var count = await GetOrFetchAsync($"Search:v1:paperCount:{queryString}",
                () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.SearchPapersAsync(queryString)));
            if (count.Data?["num"] is not JsonNode num)
            {
                // Never mix a missing count into a cached answer.
                return new(OperationResult.Failed) { Errors = count.Errors };
            }

            stats["papers"] = num.ToString();
            if (IsCount(stats["test"]))
            {
                stats["test"] = num.ToString();
            }

            return result;
        }

        private static bool IsCount(JsonNode? value) => long.TryParse(value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _);

        /// <summary>How many published papers (or study materials) a board and grade have: the routed paper list's <c>num</c>.</summary>
        private Task<ResultData<JsonObject>> PaperCountAsync(string? section, string? grade, bool isPaper)
        {
            var queryString = NormalizeQuery([new("section", section), new("base", grade), new("is_paper", isPaper ? "1" : "0"), new("perpage", "1")], PaperCountKeys);
            return GetOrFetchAsync($"Search:v1:paperCount:{queryString}",
                () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.SearchPapersAsync(queryString)));
        }

        private async Task<ResultData<JsonObject>> GetSiteCountsAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            // The same profiles the teacher directory lists (named, public teachers), so the home page count matches it.
            var teachers = await uow.GetRepository<ApplicationUser>().CountAsync(new TeacherDirectorySpecification());
            var schools = await uow.GetRepository<School>().CountAsync();
            return new(OperationResult.Succeeded) { Data = new() { ["teachers"] = teachers, ["schools"] = schools } };
        }

        /// <summary>Copies the first of <paramref name="keys"/> that holds a number (gama-api sends counts as strings).</summary>
        private static void AddCount(JsonObject stats, string name, JsonObject? source, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (long.TryParse(source?[key]?.ToString(), CultureInfo.InvariantCulture, out var count))
                {
                    stats[name] = count;
                    return;
                }
            }
        }

        private async Task<ResultData<JsonObject>> GetOrFetchAsync(string cacheKey, Func<Task<ResultData<JsonObject>>> fetch)
        {
            try
            {
                var cached = await GetCachedAsync(cacheKey);
                if (cached is not null)
                {
                    return new(OperationResult.Succeeded) { Data = cached };
                }

                var result = await fetch();
                if (result.Data is not null)
                {
                    await SetCachedAsync(cacheKey, result.Data);
                }

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>The allowed parameters with a value, sorted, so the same search always gives the same cache key.</summary>
        private static string NormalizeQuery(IEnumerable<KeyValuePair<string, StringValues>> query, FrozenSet<string> allowedKeys) => string.Join('&', query
            .Where(t => allowedKeys.Contains(t.Key))
            .Select(t => (t.Key, Value: t.Value.ToString().Trim()))
            .Where(t => t.Value.Length > 0)
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => $"{t.Key}={Uri.EscapeDataString(t.Value.Length > MaxValueLength ? t.Value[..MaxValueLength] : t.Value)}"));

        // A cache outage only costs the gama-api call, so neither read nor write may fail the search.
        private async Task<JsonObject?> GetCachedAsync(string cacheKey)
        {
            try
            {
                return await cacheProvider.Value.GetAsync<JsonObject>(cacheKey);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        private async Task SetCachedAsync(string cacheKey, JsonObject data)
        {
            try
            {
                await cacheProvider.Value.SetAsync(cacheKey, data, CacheOptions);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }
    }
}
