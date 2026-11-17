namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.TopicMatch;

    /// <summary>
    /// Copies gama-api's suggested topic matches (<c>topic_found_matches</c>) into <c>TopicMatches</c>, reading gama-api's
    /// database only, mirroring it (ids kept; a match gone from gama-api is removed). It refuses to run once the topic matches
    /// are reviewed here (<c>Migration:WriteRoutes:Types.TopicMatches</c> is <c>Native</c>). See docs/business/tutorials.md,
    /// "Topic matching".
    /// </summary>
    [Injectable]
    public interface ITopicMatchImportService
    {
        Task<ResultData<TopicMatchImportResultDto>> ImportAsync();
    }
}
