namespace GamaEdtech.Presentation.ViewModel.School
{
    public sealed class SchoolCommentsResponseViewModel
    {
        public long Id { get; set; }
        public string? CreationUser { get; set; }
        public string? CreationUserAvatarUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? Comment { get; set; }
        public double AverageRate { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public bool LikedByCurrentUser { get; set; }
        public bool DislikedByCurrentUser { get; set; }
    }
}
