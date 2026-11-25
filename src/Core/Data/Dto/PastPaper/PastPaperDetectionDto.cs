namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>
    /// What a question paper's PDF says about itself (since 2026-11-25), read when it is uploaded, so the add form can fill
    /// itself in for the teacher to confirm. Each found field is a lookup item (id and title) the form takes as it is; what
    /// isn't found is null. See docs/business/past-papers.md, "Filling the form from the paper".
    /// </summary>
    public sealed class PastPaperDetectionDto
    {
        /// <summary><c>pattern</c>: read from the cover page's printed codes; <c>ai</c>: an AI model read the page.</summary>
        public required string Source { get; set; }

        /// <summary>The file is a mark scheme, not a question paper (it was likely uploaded in the wrong place).</summary>
        public bool IsMarkScheme { get; set; }

        /// <summary>What the file is, by what its first page prints: <c>question</c>, <c>answer</c> (a mark scheme) or <c>extra</c>
        /// (then <see cref="FileType"/> says which: Insert, Confidential Instructions, Transcript, Pre Released Material, Solved
        /// Paper).</summary>
        public string? FileRole { get; set; }

        /// <summary>An extra file's kind (an extra file type item), when <see cref="FileRole"/> is <c>extra</c>.</summary>
        public DetectedItemDto? FileType { get; set; }

        public DetectedItemDto? Board { get; set; }

        public DetectedItemDto? Grade { get; set; }

        public DetectedItemDto? Subject { get; set; }

        /// <summary>The paper type (e.g. Paper 1).</summary>
        public DetectedItemDto? Classification { get; set; }

        public DetectedItemDto? Variant { get; set; }

        public int? Year { get; set; }

        public int? Month { get; set; }

        /// <summary>The paper's code as printed, e.g. <c>9700/11</c>.</summary>
        public string? Code { get; set; }

        /// <summary>A title in the site's usual form, e.g. <c>Biology 9700/11 May June 2025</c>.</summary>
        public string? Title { get; set; }

        public sealed class DetectedItemDto
        {
            public required int Id { get; set; }

            public required string Title { get; set; }
        }
    }
}
