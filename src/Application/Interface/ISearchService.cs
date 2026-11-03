namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.Extensions.Primitives;

    [Injectable]
    public interface ISearchService
    {
        /// <summary>gama-api's public search (or its per-type counts with <paramref name="typesStats"/>) for the site's search
        /// page, cached for everyone: it is the same for every visitor.</summary>
        Task<ResultData<JsonObject>> SearchAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query, bool typesStats);

        /// <summary>gama-api's public paper list (<c>tests/search</c>) for the subject directory, cached for everyone the
        /// same way. Unlike a paper's own page (<c>tests/{id}</c>), fetching it counts no views, so caching it is safe.</summary>
        Task<ResultData<JsonObject>> SearchPapersAsync([NotNull] IEnumerable<KeyValuePair<string, StringValues>> query);

        /// <summary>gama-api's public data of one past paper (<c>tests/{id}</c>) for its page, fetched anonymously and cached for
        /// everyone. Fetched anonymously, so it holds nothing per user.</summary>
        Task<ResultData<JsonObject>> GetPaperAsync(long id);

        /// <summary>The home page's counts for a board (<paramref name="section"/>) and grade (<paramref name="grade"/>) in one answer:
        /// gama-api's papers, study materials, exams and tutorials, plus this app's public teacher profiles and schools. A count that
        /// couldn't be fetched is left out, and the result is then marked failed so it isn't cached.</summary>
        Task<ResultData<JsonObject>> GetHomeStatsAsync(string? section, string? grade);
    }
}
