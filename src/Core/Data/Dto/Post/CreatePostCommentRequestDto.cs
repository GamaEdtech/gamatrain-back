namespace GamaEdtech.Data.Dto.Post
{
    public sealed class CreatePostCommentRequestDto
    {
        public required long PostId { get; set; }
        public required long UserId { get; set; }
        public string? Comment { get; set; }
    }
}
