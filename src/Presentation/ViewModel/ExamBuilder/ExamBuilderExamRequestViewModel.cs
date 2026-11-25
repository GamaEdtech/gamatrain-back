namespace GamaEdtech.Presentation.ViewModel.ExamBuilder
{
    /// <summary>An exam's details: a new draft, or a change to one of the caller's exams.</summary>
    public sealed class ExamBuilderExamRequestViewModel
    {
        public int BoardId { get; set; }

        public int GradeId { get; set; }

        public int? CourseId { get; set; }

        public int SubjectId { get; set; }

        /// <summary>The exam type (a type id, e.g. Paper 1).</summary>
        public int PaperId { get; set; }

        public int DurationMinutes { get; set; }

        public string? Title { get; set; }

        public bool NegativeMarking { get; set; }

        public int? Level { get; set; }

        public int? Year { get; set; }

        public int? SessionMonth { get; set; }

        /// <summary>The past paper the exam is built from, if any.</summary>
        public long? PastPaperId { get; set; }
    }
}
