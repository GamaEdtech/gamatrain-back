namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Blog;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IBlogService
    {
        Task<ResultData<ListDataSource<PostsDto>>> GetPostsAsync(ListRequestDto<Post>? requestDto = null);
        Task<ResultData<PostDto>> GetPostAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<long>> ManagePostContributionAsync([NotNull] ManagePostContributionRequestDto requestDto);
        Task<ResultData<bool>> RemovePostAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<bool>> LikePostAsync([NotNull] PostReactionRequestDto requestDto);
        Task<ResultData<bool>> DislikePostAsync([NotNull] PostReactionRequestDto requestDto);
        Task<ResultData<bool>> PostExistsAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<bool>> ConfirmPostContributionAsync([NotNull] ConfirmPostContributionRequestDto requestDto);
        Task<ResultData<bool>> IsCreatorOfPostAsync(long postId, int userId);

        Task<ResultData<bool>> UpdatePostReactionsAsync(long? postId = null);
    }
}
