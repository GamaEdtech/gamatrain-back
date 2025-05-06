namespace GamaEdtech.Presentation.ViewModel.School
{
    public sealed class SchoolImageInfoViewModel
    {
        public long Id { get; set; }
        public Uri? FileUri { get; set; }
        public int CreationUserId { get; set; }
        public string? CreationUser { get; set; }
        public string? TagName { get; set; }
        public string? TagIcon { get; set; }
        public long? TagId { get; set; }
    }
}
