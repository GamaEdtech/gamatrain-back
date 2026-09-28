namespace GamaEdtech.Common.Data.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public static class EnumerationExtensions
    {
        public static IEnumerable<TEnum?>? GetAll<TEnum, TKey>()
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>
        {
            var fields = typeof(TEnum).GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            return fields.Select(t => t.GetValue(null) as TEnum);
        }

        public static IEnumerable<object?>? GetAll([NotNull] Type type)
        {
            var fields = type.GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            return fields.Select(t => t.GetValue(null));
        }

        public static IEnumerable<string?>? GetNames<TEnum, TKey>()
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => GetNames(typeof(TEnum));

        public static IEnumerable<string?>? GetNames([NotNull] Type type)
        {
            var fields = type.GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            return fields.Select(t => t.GetValue(null)?.ToString());
        }

        public static bool TryGetFromNameOrValue<TEnum, TKey>(this string? nameOrValue, out TEnum? enumeration)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => TryParse<TEnum, TKey>(t => t.Name.Equals(nameOrValue, StringComparison.OrdinalIgnoreCase), out enumeration) ||
                   (int.TryParse(nameOrValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && TryConvertKey<TKey>(value, out var key)
                        && TryParse<TEnum, TKey>(t => t.Value.CompareTo(key) == 0, out enumeration));

        public static TEnum ToEnumeration<TEnum, TKey>(this TKey value)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>
        {
            var item = GetAll<TEnum, TKey>()?.FirstOrDefault(t => value.Equals(t!.Value));
            return item is null ? throw new ArgumentOutOfRangeException(nameof(value)) : item;
        }

        public static TEnum ToEnumeration<TEnum, TKey>([NotNull] this string name)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>
        {
            var item = GetAll<TEnum, TKey>()?.FirstOrDefault(t => t?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
            return item is null ? throw new ArgumentOutOfRangeException(nameof(name)) : item;
        }

        public static PropertyBuilder<TEnum?> OwnEnumeration<TEntity, TEnum, TKey>([NotNull] this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> property)
            where TEntity : class
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey> => builder.Property(property).HasConversion(t => t!.Value, t => t.ToEnumeration<TEnum, TKey>());

        /// <summary>
        /// Converts a parsed numeric value to the enumeration's own key type. A plain <c>(TKey)(object)value</c>
        /// unbox only works when <typeparamref name="TKey"/> is exactly <see cref="int"/> - for every byte-keyed
        /// enumeration it threw <see cref="InvalidCastException"/>, which silently broke the Stripe plan-switch
        /// webhook (its <c>targetBillingInterval</c> metadata is numeric) from 2026-09-20 to 2026-09-28. A value out
        /// of the key type's range (e.g. 300 for a byte key) is simply "no match", not an error.
        /// </summary>
        private static bool TryConvertKey<TKey>(int value, [MaybeNullWhen(false)] out TKey key)
        {
            try
            {
                key = (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exc) when (exc is InvalidCastException or OverflowException or FormatException)
            {
                key = default;
                return false;
            }
        }

        private static bool TryParse<TEnum, TKey>([NotNull] Func<TEnum, bool> predicate, out TEnum? enumeration)
            where TEnum : Enumeration<TEnum, TKey>
            where TKey : IEquatable<TKey>, IComparable<TKey>
        {
            enumeration = GetAll<TEnum, TKey>()?.FirstOrDefault(predicate!);
            return enumeration is not null;
        }
    }
}
