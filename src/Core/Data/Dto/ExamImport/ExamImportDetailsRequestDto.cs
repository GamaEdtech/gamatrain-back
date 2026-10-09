namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// Every detail of a draft exam at once (a field left out is unset), so changing the board, grade or course never
    /// keeps a subject from the earlier choice. The ids are checked against gama-api as a chain.
    /// </summary>
    public sealed class ExamImportDetailsRequestDto
    {
        public long? ExamId { get; set; }

        public required int BoardId { get; set; }

        public required int GradeId { get; set; }

        public int? CourseId { get; set; }

        public required int SubjectId { get; set; }

        public required int PaperId { get; set; }

        public required int DurationMinutes { get; set; }

        public string? Component { get; set; }

        public int? SessionMonth { get; set; }

        public int? Year { get; set; }

        public string? Title { get; set; }

        public int? Level { get; set; }

        public bool NegativeMarking { get; set; }

        public long? PastPaperId { get; set; }
    }
}
