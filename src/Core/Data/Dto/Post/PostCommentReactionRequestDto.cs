namespace GamaEdtech.Data.Dto.Post
{
    public sealed class PostCommentReactionRequestDto
    {
        public required long PostId { get; set; }
        public required long CommentId { get; set; }
        public required long UserId { get; set; }
    }
}
