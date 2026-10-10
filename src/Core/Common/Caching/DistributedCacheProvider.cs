namespace GamaEdtech.Common.Caching
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Caching.StackExchangeRedis;
    using Microsoft.Extensions.Options;
    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Common.Data.Enumeration;

    using StackExchange.Redis;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class DistributedCacheProvider : ICacheProvider
    {
        private readonly IDistributedCache cache;
        private readonly Lazy<IConnectionMultiplexer> redis;
        private readonly string? instanceName;
        private readonly JsonSerializerOptions jsonSerializerOptions;

        /// <summary><paramref name="redis"/> is the connection <paramref name="cache"/> (a <see cref="RedisCache"/>) uses too,
        /// for what <see cref="IDistributedCache"/> can't do in one step.</summary>
        public DistributedCacheProvider(IDistributedCache cache, Lazy<IConnectionMultiplexer> redis, [NotNull] IOptions<RedisCacheOptions> redisOptions)
        {
            this.cache = cache;
            this.redis = redis;
            instanceName = redisOptions.Value.InstanceName;
            jsonSerializerOptions = new JsonSerializerOptions();
            jsonSerializerOptions.Converters.Add(new BitArrayConverter());
            jsonSerializerOptions.Converters.Add(new UlidJsonConverter());
        }

        public async Task<TItem?> GetAsync<TItem, TEnum, TKey>([NotNull] TEnum key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => await GetAsync(key.Name, factory, options, tenant);

        public async Task<TItem?> GetAsync<TItem, TEnum>(TEnum key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : struct => await GetAsync(key.ToString()!, factory, options, tenant);

        public async Task<TItem?> GetAsync<TItem>([NotNull] string key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
        {
            var cacheKey = GenerateKey(key, tenant);
            var tmp = await cache.GetAsync(cacheKey);
            if (tmp is null)
            {
                if (factory is null)
                {
                    return default;
                }

                options ??= new DistributedCacheEntryOptions();

                var result = await factory();
                await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result, jsonSerializerOptions), options);

                return result;
            }

            using var stream = new MemoryStream(tmp);
            return await JsonSerializer.DeserializeAsync<TItem?>(stream, jsonSerializerOptions);
        }

        public TItem? Get<TItem, TEnum, TKey>([NotNull] TEnum key, Func<TItem?>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => Get(key.Name, factory, options, tenant);

        public TItem? Get<TItem, TKey>(TKey key, Func<TItem?>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct => Get(key.ToString()!, factory, options, tenant);

        public TItem? Get<TItem>([NotNull] string key, Func<TItem?>? factory, DistributedCacheEntryOptions? options = null, string? tenant = null)
        {
            var cacheKey = GenerateKey(key, tenant);
            var tmp = cache.Get(cacheKey);
            if (tmp is null)
            {
                if (factory is null)
                {
                    return default;
                }

                options ??= new DistributedCacheEntryOptions();

                var result = factory();
                cache.Set(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result, jsonSerializerOptions), options);

                return result;
            }

            using var stream = new MemoryStream(tmp);
            return JsonSerializer.Deserialize<TItem?>(stream);
        }

        public async Task RemoveAsync<TEnum, TKey>([NotNull] TEnum key, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => await RemoveAsync(key.Name, tenant);

        public async Task RemoveAsync<TKey>(TKey key, string? tenant = null)
            where TKey : struct => await RemoveAsync(key.ToString()!, tenant);

        public async Task RemoveAsync([NotNull] string key, string? tenant = null) => await cache.RemoveAsync(GenerateKey(key, tenant));

        public async Task<IAsyncDisposable?> LockAsync([NotNull] string key, TimeSpan lifetime, TimeSpan wait, string? tenant = null)
        {
            var lockKey = $"{instanceName}lock_{GenerateKey(key, tenant)}";
            var holder = Guid.NewGuid().ToString("N");
            var database = redis.Value.GetDatabase();
            var deadline = DateTimeOffset.UtcNow + wait;
            while (!await database.LockTakeAsync(lockKey, holder, lifetime))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            return new RedisLock(database, lockKey, holder);
        }

        public async Task<TItem?> GetAndRemoveAsync<TItem>([NotNull] string key, string? tenant = null)
        {
            // Concurrent callers may all read the item, but Redis deletes a key only once (RedisCache prefixes it with its
            // instance name): the value goes only to the caller whose delete removed it.
            var cacheKey = GenerateKey(key, tenant);
            var value = await cache.GetAsync(cacheKey);
            return value is not null && await redis.Value.GetDatabase().KeyDeleteAsync($"{instanceName}{cacheKey}")
                ? JsonSerializer.Deserialize<TItem?>(value, jsonSerializerOptions)
                : default;
        }

        public void Remove<TEnum, TKey>([NotNull] TEnum key, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => Remove(key.Name, tenant);

        public void Remove<TKey>(TKey key, string? tenant = null)
            where TKey : struct => Remove(key.ToString()!, tenant);

        public void Remove([NotNull] string key, string? tenant = null) => cache.Remove(GenerateKey(key, tenant));

        public async Task SetAsync<TItem, TEnum, TKey>([NotNull] TEnum key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => await SetAsync(key.Name, value, options, tenant);

        public async Task SetAsync<TItem, TKey>(TKey key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct => await SetAsync(key.ToString()!, value, options, tenant);

        public async Task SetAsync<TItem>([NotNull] string key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
        {
            options ??= new DistributedCacheEntryOptions();

            await cache.SetAsync(GenerateKey(key, tenant), JsonSerializer.SerializeToUtf8Bytes(value, jsonSerializerOptions), options);
        }

        public void Set<TItem, TEnum, TKey>([NotNull] TEnum key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => Set(key.Name, value, options, tenant);

        public void Set<TItem, TKey>(TKey key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct => Set(key.ToString()!, value, options, tenant);

        public void Set<TItem>([NotNull] string key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
        {
            options ??= new DistributedCacheEntryOptions();

            cache.Set(GenerateKey(key, tenant), JsonSerializer.SerializeToUtf8Bytes(value, jsonSerializerOptions), options);
        }

        private static string GenerateKey([NotNull] string key, string? tenant = null) => tenant.IsNullOrEmpty() ? key : tenant + "_" + key;

        /// <summary>A lock taken by <see cref="LockAsync"/>: releasing it deletes the key only while this holder still has it.</summary>
        private sealed class RedisLock(IDatabase database, RedisKey key, RedisValue holder) : IAsyncDisposable
        {
            // A transaction instead of a script, so it also works where scripting is unavailable.
            public async ValueTask DisposeAsync() => _ = await database.LockReleaseAsync(key, holder);
        }
    }
}
