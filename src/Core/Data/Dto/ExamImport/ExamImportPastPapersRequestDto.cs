namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>Past papers on gamatrain: the ones with these details, or the latest ones (<see cref="Latest"/>).</summary>
    public sealed class ExamImportPastPapersRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public int? Year { get; set; }

        public int? SessionMonth { get; set; }

        /// <summary>Newest first, papers of any kind; otherwise only papers (no other files).</summary>
        public bool Latest { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; }
    }
}
