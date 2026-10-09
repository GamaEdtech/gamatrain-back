namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>An image to upload for a question: its bytes, or a public http(s) link to download it from.</summary>
    public sealed class AddExamImportFigureRequestDto
    {
#pragma warning disable CA1819 // Properties should not return arrays
        public byte[]? Content { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public Uri? Url { get; set; }

        public string? Name { get; set; }
    }
}
