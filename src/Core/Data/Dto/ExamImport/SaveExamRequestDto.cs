namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A draft exam to create on gama-api, or the new details of draft <see cref="ExamId"/>.</summary>
    public sealed class SaveExamRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The draft to change; null creates one.</summary>
        public long? ExamId { get; set; }

        public int BoardId { get; set; }

        public int GradeId { get; set; }

        public int? CourseId { get; set; }

        public int SubjectId { get; set; }

        public int PaperId { get; set; }

        public int DurationMinutes { get; set; }

        public required string Title { get; set; }

        public bool NegativeMarking { get; set; }

        public int? Level { get; set; }

        public int? Year { get; set; }

        public int? SessionMonth { get; set; }

        /// <summary>The past paper to link the exam to (gama-api links it when the draft is created).</summary>
        public long? PastPaperId { get; set; }
    }
}
