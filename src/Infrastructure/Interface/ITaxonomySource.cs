namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The lookup lists of <see cref="MigrationDomain.Types"/> (boards, grades, subjects, topics, paper types). Never call a
    /// source directly: go through <c>IMigrationRouter</c>, which picks the gama-api proxy or the native one from the
    /// domain's <see cref="MigrationRoute"/>. A source is also called outside a request (the shadow comparison), so it
    /// takes everything it needs from its arguments, not from the current HTTP request.
    /// </summary>
    [Injectable]
    public interface ITaxonomySource : IProvider<MigrationRoute>
    {
        /// <summary>Every board, as its gama-api id and title.</summary>
        Task<ResultData<IEnumerable<KeyValuePair<int, string?>>>> GetBoardsAsync();

        /// <summary>The options of one kind (board, grade, course, subject, topic, paper, classification) under a parent.</summary>
        Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetExamOptionsAsync([NotNull] ExamImportOptionsRequestDto requestDto);

        /// <summary>One of gama-api's lookup lists (<c>types/list</c>), anonymously, with all its fields.</summary>
        Task<ResultData<IReadOnlyList<TaxonomyItemDto>>> GetTypeItemsAsync([NotNull] TaxonomyListRequestDto requestDto);
    }
}
