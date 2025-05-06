namespace GamaEdtech.Presentation.ViewModel.Blog
{
    using System;

    public sealed class PostsResponseViewModel
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public Uri? ImageUri { get; set; }
    }
}
