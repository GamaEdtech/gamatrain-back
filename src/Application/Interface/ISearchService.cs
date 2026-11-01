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
    }
}
