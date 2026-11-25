namespace GamaEdtech.Presentation.ViewModel.ExamBuilder
{
    /// <summary>One of the caller's exams (or a public one) in the exam builder.</summary>
    public sealed class ExamBuilderExamViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public int? BoardId { get; set; }

        public string? Board { get; set; }

        public int? GradeId { get; set; }

        public string? Grade { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public string? Subject { get; set; }

        /// <summary>The exam type (a type id, e.g. Paper 1).</summary>
        public int? PaperId { get; set; }

        public string? Paper { get; set; }

        public int? DurationMinutes { get; set; }

        public int? Level { get; set; }

        public int? Year { get; set; }

        public int? SessionMonth { get; set; }

        public bool NegativeMarking { get; set; }

        public long? PastPaperId { get; set; }

        public IReadOnlyList<long> QuestionIds { get; set; } = [];

        /// <summary>The caller owns it.</summary>
        public bool Owner { get; set; }

        /// <summary>gama-api's status: 6 draft, 7 published by its owner, 1 confirmed, 5 cloned, 0 waiting, 2 rejected.</summary>
        public int Status { get; set; }
    }
}
