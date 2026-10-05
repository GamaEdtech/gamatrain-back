namespace GamaEdtech.Test.Core
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    // FilterListAsync used to re-order every query by its SortFilter, or Id desc by default, which silently replaced a
    // caller's own OrderBy. The admin ticket list (unread first, then latest activity) came back in Id order.
    public class FilterListAsyncTests
    {
        public sealed class Row
        {
            public long Id { get; set; }
            public bool IsRead { get; set; }
            public DateTimeOffset LastActivity { get; set; }
        }

        private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

        // Id order differs from the business order on purpose: the newest activity is on the OLDEST row.
        private static IQueryable<Row> Rows() => new[]
        {
            new Row { Id = 1, IsRead = false, LastActivity = Now.AddMinutes(-1) },
            new Row { Id = 2, IsRead = true, LastActivity = Now.AddDays(-1) },
            new Row { Id = 3, IsRead = false, LastActivity = Now.AddDays(-3) },
            new Row { Id = 4, IsRead = true, LastActivity = Now.AddDays(-2) },
        }.AsQueryable();

        private static PagingDto Page(int size = 10, SortFilter? sort = null) => new()
        {
            PageFilter = new() { Skip = 0, Size = size, ReturnTotalRecordsCount = true },
            SortFilter = sort is null ? null : [sort],
        };

        [Fact]
        public async Task PreOrderedQueryKeepsItsOrderWhenNoSortIsRequested()
        {
            var (list, total) = await Rows().OrderBy(t => t.IsRead).ThenByDescending(t => t.LastActivity).FilterListAsync(Page());

            Assert.Equal([1L, 3, 2, 4], list.Select(t => t.Id));
            Assert.Equal(4, total);
        }

        [Fact]
        public async Task PreOrderedQueryIsPagedInItsOwnOrder()
        {
            var (list, _) = await Rows().OrderBy(t => t.IsRead).ThenByDescending(t => t.LastActivity).FilterListAsync(Page(size: 2));

            Assert.Equal([1L, 3], list.Select(t => t.Id));
        }

        [Fact]
        public async Task PreOrderedQueryKeepsItsOrderWithASearchFilter()
        {
            var paging = Page();
            paging.SearchFilter = [new() { Column = "IsRead", Phrase = "false" }];

            var (list, _) = await Rows().OrderBy(t => t.IsRead).ThenByDescending(t => t.LastActivity).FilterListAsync(paging);

            Assert.Equal([1L, 3], list.Select(t => t.Id));
        }

        [Fact]
        public async Task ExplicitSortStillWins()
        {
            var (list, _) = await Rows().OrderBy(t => t.IsRead).FilterListAsync(Page(sort: new() { Column = "Id", SortType = SortType.Asc }));

            Assert.Equal([1L, 2, 3, 4], list.Select(t => t.Id));
        }

        [Fact]
        public async Task UnorderedQueryDefaultsToIdDesc()
        {
            var (list, _) = await Rows().Where(t => t.Id > 0).FilterListAsync(Page());

            Assert.Equal([4L, 3, 2, 1], list.Select(t => t.Id));
        }
    }
}
