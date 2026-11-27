namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Identity;

    [Injectable]
    public interface IPostProfileContentService
    {
        Task<ResultData<ListDataSource<PublicProfilePostDto>>> GetPublishedByOwnerAsync(long ownerUserId, int limit);
    }

    [Injectable]
    public interface IPastPaperProfileContentService
    {
        Task<ResultData<ListDataSource<PublicProfilePastPaperDto>>> GetPublishedByOwnerAsync(long ownerUserId, long? coreOwnerId, int limit);
    }

    [Injectable]
    public interface IExamProfileContentService
    {
        Task<ResultData<ListDataSource<PublicProfileExamDto>>> GetPublishedByOwnerAsync(long ownerUserId, long? coreOwnerId, int limit);
    }
}
