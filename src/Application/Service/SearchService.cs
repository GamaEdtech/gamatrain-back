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

        // gama-api's search types that are past papers.
        private static readonly FrozenSet<string> PaperTypes = new[] { "test", "paper", "papers" }.ToFrozenSet(StringComparer.Ordinal);

        private static readonly FrozenSet<string> PaperQueryKeys = new[]
        {
            "page", "perpage", "lesson", "is_paper", "directory", "edu_year", "edu_month", "test_type", "variant",
        }.ToFrozenSet(StringComparer.Ordinal);

        private const int MaxValueLength = 200;

        // A new paper shows up within 5 minutes; the API answer is also cached by Cloudflare (see SearchController).
        private static readonly DistributedCacheEntryOptions CacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

        public Task<ResultData<JsonObject>> SearchAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query, bool typesStats)
        {
            var queryString = NormalizeQuery(query, QueryKeys);
            // Versioned: bump it if what is cached changes shape.
            var cacheKey = $"Search:v1:{(typesStats ? "stats" : "list")}:{queryString}";

            // gama-api answers it all while past papers are on gama-api. Once their route moves, the papers in it (the past
            // paper and study material lists, and every "papers" count) follow that route; everything else stays gama-api's.
            if (migrationRouter.Value.GetRoute(MigrationDomain.PastPapers, MigrationFeatures.Website) == MigrationRoute.Legacy)
            {
                return GetOrFetchAsync(cacheKey, () => coreProvider.Value.SearchAsync(queryString, typesStats));
            }

            var parsed = QueryHelpers.ParseQuery(queryString);
            return GetOrFetchAsync(cacheKey, (typesStats, PaperTypes.Contains(parsed.GetValueOrDefault("type").ToString())) switch
            {
                (true, _) => () => TypesStatsAsync(queryString, parsed),
                (false, true) => () => PaperSearchAsync(parsed),
                _ => () => OtherSearchAsync(queryString, parsed),
            });
        }

        public Task<ResultData<JsonObject>> SearchPapersAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query)
        {
            var queryString = NormalizeQuery(query, PaperQueryKeys);
            return GetOrFetchAsync($"Search:v1:papers:{queryString}", () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.SearchPapersAsync(queryString)));
        }

        public Task<ResultData<JsonObject>> GetPaperAsync(long id)
            => GetOrFetchAsync($"Search:v1:paper:{id}", () => migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Website, (IPastPaperSource source) => source.GetPaperAsync(id)));

        public async Task<ResultData<JsonObject>> GetHomeStatsAsync(string? section, string? grade)
        {
            KeyValuePair<string, StringValues>[] filters = [new("section", section), new("base", grade)];
            // Each part is cached on its own: the counts per board and grade, the site counts once for all. Papers and study
            // materials are the paper list's totals, so they follow the past papers' route, which is our table once Native.
            // Exams and tutorials still come from gama-api's typesstats.
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

        /// <summary>Another type's search (exams, tutorials...): gama-api's answer, with its <c>types_stats</c> papers count
        /// taken from the routed paper list.</summary>
        private async Task<ResultData<JsonObject>> OtherSearchAsync(string queryString, Dictionary<string, StringValues> parsed)
        {
            var result = await coreProvider.Value.SearchAsync(queryString, typesStats: false);
            return result.Data?["types_stats"] is JsonObject stats ? await WithOurPaperCountAsync(result, stats, parsed) : result;
        }

        /// <summary>gama-api's <c>typesstats</c> with its <c>papers</c> count (and the older <c>test</c> alias) taken from the
        /// routed paper list for the same filters. A count gama-api leaves out (the searched type's own) stays out.</summary>
        private async Task<ResultData<JsonObject>> TypesStatsAsync(string queryString, Dictionary<string, StringValues> parsed)
        {
            var result = await coreProvider.Value.SearchAsync(queryString, typesStats: true);
            return result.Data is { } stats ? await WithOurPaperCountAsync(result, stats, parsed) : result;
        }

        private async Task<ResultData<JsonObject>> WithOurPaperCountAsync(ResultData<JsonObject> result, JsonObject stats, Dictionary<string, StringValues> parsed)
        {
            if (!IsCount(stats["papers"]))
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

            static bool IsCount(JsonNode? value) => long.TryParse(value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }

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
