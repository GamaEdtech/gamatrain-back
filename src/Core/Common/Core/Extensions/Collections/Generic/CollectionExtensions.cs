namespace GamaEdtech.Common.Core.Extensions.Collections.Generic
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    public static class CollectionExtensions
    {
        public static bool IsNullOrEmpty<T>([MaybeNull] this ICollection<T> source) => source is null || source.Count <= 0;

        public static bool AddIfNotContains<T>([NotNull] this ICollection<T> source, T item)
        {
            if (source.Contains(item))
            {
                return false;
            }

            source.Add(item);
            return true;
        }

        public static IEnumerable<T> AddIfNotContains<T>([NotNull] this ICollection<T> source, [NotNull] IEnumerable<T> items)
        {
            var addedItems = new List<T>();

            foreach (var item in items)
            {
                if (source.Contains(item))
                {
                    continue;
                }

                source.Add(item);
                addedItems.Add(item);
            }

            return addedItems;
        }

        public static bool AddIfNotContains<T>([NotNull] this ICollection<T> source, [NotNull] Func<T, bool> predicate, [NotNull] Func<T> itemFactory)
        {
            if (source.Any(predicate))
            {
                return false;
            }

            source.Add(itemFactory());
            return true;
        }

        public static IList<T> RemoveAll<T>([NotNull] this ICollection<T> source, Func<T, bool> predicate)
        {
            var items = source.Where(predicate).ToList();

            foreach (var item in items)
            {
                _ = source.Remove(item);
            }

            return items;
        }

        public static T? Find<T>([NotNull] this IReadOnlyList<T> list, Predicate<T> match)
        {
            ArgumentNullException.ThrowIfNull(match);

            for (var i = 0; i < list.Count; i++)
            {
                if (match(list[i]))
                {
                    return list[i];
                }
            }

            return default;
        }

        public static bool Exists<T>([NotNull] this ReadOnlyCollection<T> list, Predicate<T> match)
        {
            ArgumentNullException.ThrowIfNull(match);

            for (var i = 0; i < list.Count; i++)
            {
                if (match(list[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Exists<T>([NotNull] this Collection<T> list, Predicate<T> match)
        {
            ArgumentNullException.ThrowIfNull(match);

            for (var i = 0; i < list.Count; i++)
            {
                if (match(list[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
