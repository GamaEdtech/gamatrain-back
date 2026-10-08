namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// The exam details of an import (<c>ExamImport.Details</c>), with gama-api's ids and titles. On gamatrain a board
    /// is gama's <c>section</c>, a grade its <c>base</c>, a subject its <c>lesson</c> and a paper its <c>exam_type</c>
    /// (Paper 1..6, Topical); the session is <c>edu_month</c> and the year <c>edu_year</c>.
    /// </summary>
    public sealed class ExamImportDetailsDto
    {
        public int? BoardId { get; set; }

        public string? Board { get; set; }

        public int? GradeId { get; set; }

        public string? Grade { get; set; }

        /// <summary>Only for boards whose grades split by course.</summary>
        public int? CourseId { get; set; }

        /// <summary>True when the board has courses and none is chosen yet.</summary>
        public bool CourseRequired { get; set; }

        public int? SubjectId { get; set; }

        public string? Subject { get; set; }

        public int? PaperId { get; set; }

        public string? Paper { get; set; }

        /// <summary>The syllabus/component code, e.g. <c>9709/12</c>: goes into the title and each question's source.</summary>
        public string? Component { get; set; }

        /// <summary>3 Feb/March, 6 May/June, 11 Oct/Nov (any 1-12 is allowed).</summary>
        public int? SessionMonth { get; set; }

        public int? Year { get; set; }

        public int? DurationMinutes { get; set; }

        public string? Title { get; set; }

        public int? Level { get; set; }

        public bool NegativeMarking { get; set; }

        /// <summary>A past paper already on gamatrain (gama's <c>tests.id</c>) the exam is linked to.</summary>
        public long? PastPaperId { get; set; }

        /// <summary>The subject's topics. When there are any, every question needs one of them.</summary>
        public IReadOnlyList<ExamImportOptionDto>? Topics { get; set; }
    }
}
