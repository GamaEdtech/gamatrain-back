namespace GamaEdtech.Common.Core.Extensions.Linq
{
    using System;
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Linq.Dynamic.Core;
    using System.Linq.Expressions;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core.Extensions.Collections;
    using GamaEdtech.Common.Data;

    using Microsoft.EntityFrameworkCore;

    using static GamaEdtech.Common.Core.Constants;

    public static class QueryableExtensions
    {
        public static IQueryable<T> PageBy<T>([NotNull] this IQueryable<T> query, int skipCount, int maxResultCount) => query.Skip(skipCount).Take(maxResultCount);

        public static TQueryable PageBy<T, TQueryable>([NotNull] this TQueryable query, int skipCount, int maxResultCount)
            where TQueryable : IQueryable<T> => (TQueryable)query.Skip(skipCount).Take(maxResultCount);

        public static IQueryable<T> WhereIf<T>([NotNull] this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate) => condition
                ? query.Where(predicate)
                : query;

        public static TQueryable WhereIf<T, TQueryable>([NotNull] this TQueryable query, bool condition, Expression<Func<T, bool>> predicate)
            where TQueryable : IQueryable<T> => condition
                ? (TQueryable)query.Where(predicate)
                : query;

        public static IQueryable<T> WhereIf<T>([NotNull] this IQueryable<T> query, bool condition, Expression<Func<T, int, bool>> predicate) => condition
                ? query.Where(predicate)
                : query;

        public static TQueryable WhereIf<T, TQueryable>([NotNull] this TQueryable query, bool condition, Expression<Func<T, int, bool>> predicate)
            where TQueryable : IQueryable<T> => condition
                ? (TQueryable)query.Where(predicate)
        : query;

        public static (IQueryable<T> List, bool Applied) OrderBy<T>(this IQueryable<T> lst, IEnumerable<SortFilter>? sortFilters)
        {
            if (sortFilters?.Any() == true)
            {
                var properties = typeof(T).GetProperties();
                var items = sortFilters.Where(t => properties.Exists(p => p.Name.Equals(t.Column, StringComparison.OrdinalIgnoreCase)));
                if (items?.Any() == true)
                {
                    var criteria = string.Join(",", items.Select(t => $"{t.Column} {t.SortType.ToString().ToLowerInvariant()}"));
                    return (lst.OrderBy(criteria), true);
                }
            }

            return (lst, false);
        }

        public static async Task<(IQueryable<TSource> List, int? TotalRecordsCount)> FilterListAsync<TSource>(this IQueryable<TSource> lst, PagingDto? pagingDto)
        {
            if (pagingDto is null)
            {
                return (lst, null);
            }

            var properties = typeof(TSource).GetProperties();

            if (pagingDto.SearchFilter?.Any() == true)
            {
                foreach (var item in pagingDto.SearchFilter)
                {
                    ApplyFilter(item);
                }
            }

            (lst, var sortApplied) = OrderBy(lst, pagingDto.SortFilter ?? [new() { Column = "Id", SortType = SortType.Desc }]);

            int? total = null;

            if (pagingDto.PageFilter is not null)
            {
                if (pagingDto.PageFilter.ReturnTotalRecordsCount)
                {
#pragma warning disable S6966 // Awaitable method should be used
                    total = lst is IEnumerable ? lst.Count() : await lst.CountAsync();
#pragma warning restore S6966 // Awaitable method should be used
                }

                if (!sortApplied)
                {
                    var id = properties.FirstOrDefault(t => t.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))?.Name ?? properties.FirstOrDefault()?.Name;
                    lst = lst.OrderBy(id!);
                }

                lst = lst.Skip(pagingDto.PageFilter.Skip).Take(pagingDto.PageFilter.Size);
            }

            return (lst, total);

            void ApplyFilter(SearchFilter searchFilter)
            {
                var property = properties.FirstOrDefault(t => t.Name.Equals(searchFilter.Column, StringComparison.OrdinalIgnoreCase));
                if (property is null)
                {
                    return;
                }

                var type = property.PropertyType;

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    var tmp = Nullable.GetUnderlyingType(type);
                    if (tmp is not null)
                    {
                        type = tmp;
                    }
                }

                if (type.IsEnum)
                {
                    var fields = type.GetFields();
                    var match = false;
                    if (fields is not null)
                    {
                        for (var i = 0; i < fields.Length; i++)
                        {
                            var info = fields[i];
                            if (info.Name.Equals(searchFilter.Phrase, StringComparison.OrdinalIgnoreCase))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", info.GetValue(info));
                                match = true;
                                break;
                            }
                        }
                    }

                    if (!match)
                    {
                        lst = lst.Where($"{property.Name} == (@0)", searchFilter.Phrase);
                    }
                }
                else
                {
                    switch (Type.GetTypeCode(type))
                    {
                        case TypeCode.Boolean:
                        {
                            if (bool.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.SByte:
                        case TypeCode.Byte:
                        {
                            if (byte.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Int16:
                        case TypeCode.UInt16:
                        {
                            if (short.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Int32:
                        case TypeCode.UInt32:
                        {
                            if (int.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        {
                            if (long.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Single:
                        {
                            if (float.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Double:
                        {
                            if (double.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.Decimal:
                        {
                            if (decimal.TryParse(searchFilter.Phrase, out var result))
                            {
                                lst = lst.Where($"{property.Name} == (@0)", result);
                            }
                        }

                        break;
                        case TypeCode.DateTime:
                            lst = lst.Where($"{property.Name} == (@0)", searchFilter.Phrase);
                            break;
                        case TypeCode.String:
                        case TypeCode.Char:
                            lst = lst.Where($"{property.Name}.Contains(@0)", searchFilter.Phrase);
                            break;
                    }
                }
            }
        }
    }
}
