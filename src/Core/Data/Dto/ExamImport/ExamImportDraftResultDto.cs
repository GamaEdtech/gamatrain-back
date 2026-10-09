namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>What <c>publish_exam</c> and <c>discard_draft</c> did to a draft exam on gama-api.</summary>
    public sealed class ExamImportDraftResultDto
    {
        public long ExamId { get; set; }

        /// <summary>The questions the published exam has.</summary>
        public int? Questions { get; set; }

        /// <summary>The published exam.</summary>
        public Uri? ExamUrl { get; set; }

        public bool Deleted { get; set; }

        public IReadOnlyList<long>? DeletedQuestions { get; set; }

        public IReadOnlyList<string>? Errors { get; set; }
    }
}
