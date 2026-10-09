namespace GamaEdtech.Common.Caching
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.Extensions.Caching.Distributed;
    using GamaEdtech.Common.Data.Enumeration;

    [Injectable]
    public interface ICacheProvider
    {
        Task<TItem?> GetAsync<TItem, TEnum, TKey>(TEnum key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        Task<TItem?> GetAsync<TItem, TKey>(TKey key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct;

        Task<TItem?> GetAsync<TItem>([NotNull] string key, Func<Task<TItem?>>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null);

        TItem? Get<TItem, TEnum, TKey>(TEnum key, Func<TItem?>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        TItem? Get<TItem, TKey>(TKey key, Func<TItem?>? factory = null, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct;

        TItem? Get<TItem>([NotNull] string key, Func<TItem?>? factory, DistributedCacheEntryOptions? options = null, string? tenant = null);

        Task RemoveAsync<TEnum, TKey>(TEnum key, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        Task RemoveAsync<TKey>(TKey key, string? tenant = null)
            where TKey : struct;

        Task RemoveAsync([NotNull] string key, string? tenant = null);

        /// <summary>
        /// Reads an item and removes it in one step: of several concurrent callers, only the one whose removal deleted the
        /// item gets it, the rest get <see langword="default"/>. For single-use values (an OAuth authorization code).
        /// </summary>
        Task<TItem?> GetAndRemoveAsync<TItem>([NotNull] string key, string? tenant = null);

        void Remove<TEnum, TKey>(TEnum key, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        void Remove<TKey>(TKey key, string? tenant = null)
            where TKey : struct;

        void Remove([NotNull] string key, string? tenant = null);

        Task SetAsync<TItem, TEnum, TKey>(TEnum key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        Task SetAsync<TItem, TKey>(TKey key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct;

        Task SetAsync<TItem>([NotNull] string key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null);

        void Set<TItem, TEnum, TKey>(TEnum key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>;

        void Set<TItem, TKey>(TKey key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null)
            where TKey : struct;

        void Set<TItem>([NotNull] string key, TItem? value, DistributedCacheEntryOptions? options = null, string? tenant = null);
    }
}
