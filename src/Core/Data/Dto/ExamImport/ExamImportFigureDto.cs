namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>An image stored for an import's questions.</summary>
    public sealed class ExamImportFigureDto
    {
        /// <summary>The id questions refer to (<c>figure</c>, <c>optionFigures</c>, <c>answerFigure</c>).</summary>
        public string? FigureId { get; set; }

        public string? Name { get; set; }

        public string? ContentType { get; set; }

        public int Size { get; set; }

        /// <summary>A signed link to the image, for checking it in a browser.</summary>
        public Uri? Url { get; set; }

#pragma warning disable CA1819 // Properties should not return arrays
        /// <summary>The image itself; only filled when it is served.</summary>
        public byte[]? Content { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
    }
}
