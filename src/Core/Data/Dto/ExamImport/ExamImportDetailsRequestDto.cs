namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// Every detail of a draft exam at once (a field left out is unset), so changing the board, grade or course never
    /// keeps a subject from the earlier choice. The ids are checked against gama-api as a chain; a required one that is
    /// left out or not valid under its parent is for the user to pick from gama-api's list.
    /// </summary>
    public sealed class ExamImportDetailsRequestDto
    {
        public long? ExamId { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public int? PaperId { get; set; }

        public int? DurationMinutes { get; set; }

        public string? Component { get; set; }

        public int? SessionMonth { get; set; }

        public int? Year { get; set; }

        public string? Title { get; set; }

        public int? Level { get; set; }

        public bool NegativeMarking { get; set; }

        public long? PastPaperId { get; set; }

        /// <summary>The user confirmed the details: create the draft or change it. Otherwise they are only checked.</summary>
        public bool Confirmed { get; set; }
    }
}
