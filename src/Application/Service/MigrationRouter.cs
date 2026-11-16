namespace GamaEdtech.Application.Service
{
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Migration;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class MigrationRouter(Lazy<IConfiguration> configuration, Lazy<IServiceProvider> serviceProvider,
        Lazy<IServiceScopeFactory> serviceScopeFactory, Lazy<ILogger<MigrationRouter>> logger) : IMigrationRouter
    {
        private const string RoutesSection = "Migration:Routes";
        private const string WriteRoutesSection = "Migration:WriteRoutes";
        private const string ShadowSamplePercentKey = "Migration:ShadowSamplePercent";

        // A comparison is extra work on top of a request that is already answered. Past this many at once, calls are
        // answered without being compared, so shadowing can never pile up behind a slow native source.
        private const int MaxConcurrentShadowCalls = 4;

        // The comparison's counts of one domain and feature are a long[] indexed by these (Interlocked needs a ref to each).
        private const int Matched = 0;
        private const int Mismatched = 1;
        private const int Failed = 2;
        private const int Skipped = 3;

        private static readonly SemaphoreSlim ShadowSlots = new(MaxConcurrentShadowCalls, MaxConcurrentShadowCalls);
        private static readonly ConcurrentDictionary<string, long[]> Counters = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, MigrationRoute> SwitchedRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            [MigrationRoute.Shadow.Name] = MigrationRoute.Shadow,
            [MigrationRoute.Native.Name] = MigrationRoute.Native,
        };

        private static readonly AsyncLocal<bool> ShadowCall = new();

        private static long shadowSequence;

        /// <summary>True inside a shadow comparison's call of a native source: the source must then have no side effects
        /// (a view count, for example), since gama-api has already done them for this call.</summary>
        public static bool InShadowCall => ShadowCall.Value;

        public MigrationRoute GetRoute([NotNull] MigrationDomain domain, string? feature = null) => ReadRoute(RoutesSection, domain, feature);

        public MigrationRoute GetWriteRoute([NotNull] MigrationDomain domain, string? feature = null) =>
            ReadRoute(WriteRoutesSection, domain, feature) == MigrationRoute.Native ? MigrationRoute.Native : MigrationRoute.Legacy;

        public Task<ResultData<T>> RunAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => RunAsync(GetRoute(domain, feature), domain, feature, call);

        public Task<ResultData<T>> RunWriteAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => RunAsync(GetWriteRoute(domain, feature), domain, feature, call);

        public IReadOnlyCollection<MigrationShadowStatisticsDto> GetShadowStatistics() => [.. Counters
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new MigrationShadowStatisticsDto
            {
                Key = t.Key,
                Matched = Interlocked.Read(ref t.Value[Matched]),
                Mismatched = Interlocked.Read(ref t.Value[Mismatched]),
                Failed = Interlocked.Read(ref t.Value[Failed]),
                Skipped = Interlocked.Read(ref t.Value[Skipped]),
            })];

        private async Task<ResultData<T>> RunAsync<TSource, T>(MigrationRoute route, MigrationDomain domain, string? feature, Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute>
        {
            var factory = serviceProvider.Value.GetRequiredService<IGenericFactory<TSource, MigrationRoute>>();
            var legacy = factory.GetProvider(MigrationRoute.Legacy);
            var native = route == MigrationRoute.Legacy && legacy is not null ? null : factory.GetProvider(MigrationRoute.Native);

            // The native source answers when the route says so, and also when it is the only one left (the domain's proxy
            // methods are deleted at the end of its migration).
            if (native is not null && (route == MigrationRoute.Native || legacy is null))
            {
                return await call(native);
            }

            if (legacy is null)
            {
                return new(OperationResult.Failed) { Errors = [new() { Message = $"No source is registered for {typeof(TSource).Name}." }] };
            }

            if (route == MigrationRoute.Native)
            {
                logger.Value.LogWarning("Migration route of {Key} is Native but {Source} has no native source; gama-api answered.", Key(domain, feature), typeof(TSource).Name);
            }

            var result = await call(legacy);
            if (route == MigrationRoute.Shadow && native is not null && IsSampled())
            {
                StartShadowCall(Key(domain, feature), call, result);
            }

            return result;
        }

        private MigrationRoute ReadRoute(string section, MigrationDomain domain, string? feature)
        {
            var value = string.IsNullOrEmpty(feature) ? null : configuration.Value[$"{section}:{domain.Name}.{feature}"];
            if (string.IsNullOrWhiteSpace(value))
            {
                value = configuration.Value[$"{section}:{domain.Name}"];
            }

            // Only these two names move traffic off gama-api; a typo keeps the route where it is safe.
            return value is not null && SwitchedRoutes.TryGetValue(value.Trim(), out var route) ? route : MigrationRoute.Legacy;
        }

        private static string Key(MigrationDomain domain, string? feature) => string.IsNullOrEmpty(feature) ? domain.Name : $"{domain.Name}.{feature}";

        // Every call when the setting is missing; else that share of them, spread evenly (no randomness needed).
        private bool IsSampled()
        {
            var percent = configuration.Value.GetValue<int?>(ShadowSamplePercentKey) ?? 100;
            return percent >= 100 || (percent > 0 && Interlocked.Increment(ref shadowSequence) % 100 < percent);
        }

        // Runs after the caller has its answer, in its own scope: the request's scope (and its DbContexts) is gone by then.
        private void StartShadowCall<TSource, T>(string key, Func<TSource, Task<ResultData<T>>> call, ResultData<T> legacyResult)
            where TSource : class, IProvider<MigrationRoute>
        {
            var counter = Counters.GetOrAdd(key, _ => new long[4]);
            if (!ShadowSlots.Wait(0))
            {
                _ = Interlocked.Increment(ref counter[Skipped]);
                return;
            }

            var scopeFactory = serviceScopeFactory.Value;
            var log = logger.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    ShadowCall.Value = true;
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var native = scope.ServiceProvider.GetRequiredService<IGenericFactory<TSource, MigrationRoute>>().GetProvider(MigrationRoute.Native);
                    if (native is null)
                    {
                        return;
                    }

                    var difference = MigrationShadowComparison.FirstDifference(legacyResult, await call(native));
                    if (difference is null)
                    {
                        _ = Interlocked.Increment(ref counter[Matched]);
                    }
                    else
                    {
                        _ = Interlocked.Increment(ref counter[Mismatched]);
                        log.LogWarning("Migration shadow mismatch in {Key}: {Difference}", key, difference);
                    }
                }
                catch (Exception exc)
                {
                    _ = Interlocked.Increment(ref counter[Failed]);
                    log.LogException(exc);
                }
                finally
                {
                    _ = ShadowSlots.Release();
                }
            });
        }
    }
}
