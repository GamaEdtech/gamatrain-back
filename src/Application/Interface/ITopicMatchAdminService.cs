namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.TopicMatch;

    /// <summary>
    /// The review of the suggested matches between GAMA's topics and other boards' (gama-api's topic matching): the list, adding
    /// one by hand, approving (links the two topics), taking an approval back, removing a suggestion, and asking this backend's
    /// AI for new suggestions. Changes are refused (<c>notNative</c>) until <c>Migration:WriteRoutes:Types.TopicMatches</c>
    /// (and the types' writes) are <c>Native</c>. See docs/business/tutorials.md, "Topic matching".
    /// </summary>
    [Injectable]
    public interface ITopicMatchAdminService
    {
        Task<ResultData<TopicMatchListDto>> GetListAsync([NotNull] TopicMatchListRequestDto requestDto);

        Task<ResultData<long>> AddAsync([NotNull] TopicMatchAddDto requestDto);

        Task<ResultData<bool>> ApproveAsync(long id, long userId);

        Task<ResultData<bool>> UnapproveAsync(long id);

        Task<ResultData<bool>> RemoveAsync(long id);

        Task<ResultData<TopicMatchSuggestResultDto>> SuggestAsync(int topicId);
    }
}
