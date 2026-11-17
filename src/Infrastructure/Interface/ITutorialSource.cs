namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The tutorials of <see cref="MigrationDomain.Tutorials"/>. Never call a source directly: go through
    /// <c>IMigrationRouter</c> (see <see cref="ITaxonomySource"/> for the rules a source follows). Every call is anonymous
    /// and answers gama-api's <c>data</c> as gama-api sends it. See docs/business/tutorials.md.
    /// </summary>
    [Injectable]
    public interface ITutorialSource : IProvider<MigrationRoute>
    {
        /// <summary>gama-api's <c>tutorials/{id}</c>: a published tutorial's page. Each fetch counts a view.</summary>
        Task<ResultData<JsonObject>> GetTutorialAsync(long id);

        /// <summary>gama-api's <c>tutorials/lessonTree/{subject}</c>: the subject's seasons and chapters, each with its
        /// published tutorials.</summary>
        Task<ResultData<JsonObject>> GetLessonTreeAsync(long subjectId);

        /// <summary>gama-api's <c>search?type=tutorial</c> with <paramref name="query"/> as its query string: <c>num</c> and
        /// <c>list</c> of published tutorials only (the search service builds the counts).</summary>
        Task<ResultData<JsonObject>> SearchTutorialsAsync([NotNull] string query);
    }
}
