namespace GamaEdtech.Presentation.ViewModel.Blog
{
    public sealed class PostContributionResponseViewModel
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public Uri? ImageUri { get; set; }
        public IEnumerable<int>? Tags { get; set; }
    }
}
