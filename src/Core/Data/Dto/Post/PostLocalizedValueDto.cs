namespace GamaEdtech.Data.Dto.Post
{
    public sealed class PostLocalizedValueDto
    {
        public required int LanguageId { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
    }
}
