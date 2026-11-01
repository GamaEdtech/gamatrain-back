namespace GamaEdtech.Application.Service
{
    using System;
    using System.Collections.Frozen;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;

    using static GamaEdtech.Common.Core.Constants;

    public class SearchService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<SearchService>> localizer, Lazy<ILogger<SearchService>> logger, Lazy<ICoreProvider> coreProvider,
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

        private const int MaxValueLength = 200;

        // A new paper shows up within 5 minutes; the API answer is also cached by Cloudflare (see SearchController).
        private static readonly DistributedCacheEntryOptions CacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

        public async Task<ResultData<JsonObject>> SearchAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query, bool typesStats)
        {
            try
            {
                var queryString = NormalizeQuery(query);
                // Versioned: bump it if what is cached changes shape.
                var cacheKey = $"Search:v1:{(typesStats ? "stats" : "list")}:{queryString}";

                var cached = await GetCachedAsync(cacheKey);
                if (cached is not null)
                {
                    return new(OperationResult.Succeeded) { Data = cached };
                }

                var result = await coreProvider.Value.SearchAsync(queryString, typesStats);
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
        private static string NormalizeQuery(IEnumerable<KeyValuePair<string, StringValues>> query) => string.Join('&', query
            .Where(t => QueryKeys.Contains(t.Key))
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
