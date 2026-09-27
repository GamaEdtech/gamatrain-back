namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Post;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IPostService
    {
        Task<ResultData<ListDataSource<PostsDto>>> GetPostsAsync(ListRequestDto<Post>? requestDto = null);
        Task<ResultData<IReadOnlyList<KeyValuePair<long, string?>>>> GetPostsTitleAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<PostDto>> GetPostAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<PostEditDto>> GetPostForEditAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<long>> ManagePostAsync([NotNull] ManagePostRequestDto requestDto);
        Task<ResultData<bool>> RemovePostAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<bool>> LikePostAsync([NotNull] PostReactionRequestDto requestDto);
        Task<ResultData<bool>> DislikePostAsync([NotNull] PostReactionRequestDto requestDto);
        Task<ResultData<bool>> PostExistsAsync([NotNull] ISpecification<Post> specification);
        Task<ResultData<bool>> ConfirmPostAsync(long postId, bool notifyUser);
        Task<ResultData<bool>> RejectPostAsync(long postId, string? comment, long userId);
        Task<ResultData<bool>> IsCreatorOfPostAsync(long postId, long userId);
        Task IncreasePostViewAsync(long id);

        #region Comments

        Task<ResultData<ListDataSource<PostCommentDto>>> GetPostCommentsAsync(ListRequestDto<PostComment>? requestDto = null);
        Task<ResultData<bool>> LikePostCommentAsync([NotNull] PostCommentReactionRequestDto requestDto);
        Task<ResultData<bool>> DislikePostCommentAsync([NotNull] PostCommentReactionRequestDto requestDto);
        Task<ResultData<long>> CreatePostCommentAsync([NotNull] CreatePostCommentRequestDto requestDto);
        Task<ResultData<bool>> ConfirmPostCommentAsync(long commentId, bool notifyUser);
        Task<ResultData<bool>> RejectPostCommentAsync(long commentId, string? comment, long userId);
        Task<ResultData<bool>> CommentExistsAsync([NotNull] ISpecification<PostComment> specification);

        #endregion

        #region Job

        Task<ResultData<bool>> UpdatePostReactionsAsync(long? postId = null);
        Task<ResultData<bool>> UpdatePostCommentReactionsAsync(long? postCommentId = null);

        #endregion
    }
}
