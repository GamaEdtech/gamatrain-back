namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Past papers on gamatrain: the ones with these details, or the directory's ones whose title has
    /// <see cref="Title"/> (every one without it).</summary>
    public sealed class ExamImportPastPapersRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>A piece of the title (gamatrain's paper search).</summary>
        public string? Title { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        /// <summary>The paper's classification (gama-api's <c>test_type</c>, e.g. Paper 2).</summary>
        public int? ClassificationId { get; set; }

        public int? Year { get; set; }

        public int? SessionMonth { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; }
    }
}
