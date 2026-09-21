namespace GamaEdtech.Data.Dto.Post
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostCommentDto
    {
        public long Id { get; set; }
        public long PostId { get; set; }
        public string? PostTitle { get; set; }
        public Status Status { get; set; } = Status.Confirmed;
        public string? RejectionComment { get; set; }
        public string? CreationUser { get; set; }
        public string? CreationUserAvatarUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? Comment { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public bool LikedByCurrentUser { get; set; }
        public bool DislikedByCurrentUser { get; set; }
    }
}
