namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A draft exam on gama-api (status 6), where an import's questions are saved as they come: its details with gama-api's
    /// ids and titles, and the questions on it; in the user's exam list also a published one. On gamatrain a board is
    /// gama's <c>section</c>, a grade its <c>base</c>, a subject its <c>lesson</c> and a paper its <c>exam_type</c> (Paper
    /// 1..6, Topical); the session is <c>edu_month</c> and the year <c>edu_year</c>.
    /// </summary>
    public sealed class ExamImportDraftDto
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public int? BoardId { get; set; }

        public string? Board { get; set; }

        public int? GradeId { get; set; }

        public string? Grade { get; set; }

        /// <summary>Only for boards whose grades split by course.</summary>
        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public string? Subject { get; set; }

        public int? PaperId { get; set; }

        public string? Paper { get; set; }

        /// <summary>The syllabus/component code, e.g. <c>9709/12</c>: goes into the default title (gama-api doesn't keep it).</summary>
        public string? Component { get; set; }

        /// <summary>3 Feb/March, 6 May/June, 11 Oct/Nov (any 1-12 is allowed).</summary>
        public int? SessionMonth { get; set; }

        public int? Year { get; set; }

        /// <summary>The session and year as words, e.g. May/June 2024.</summary>
        public string? Session { get; set; }

        public int? DurationMinutes { get; set; }

        public int? Level { get; set; }

        public bool NegativeMarking { get; set; }

        /// <summary>A past paper already on gamatrain (gama's <c>tests.id</c>) the exam is linked to when it is created.</summary>
        public long? PastPaperId { get; set; }

        /// <summary>The ids of the questions on the draft, in exam order.</summary>
        public IReadOnlyList<long> QuestionIds { get; set; } = [];

        /// <summary>The draft on gamatrain's exam builder: the owner sees every question there before publishing.</summary>
        public Uri? DraftUrl { get; set; }

        /// <summary>A published exam's page on gamatrain.</summary>
        public Uri? ExamUrl { get; set; }

        /// <summary>The subject's topics, when the details were just set. When there are any, every question needs one.</summary>
        public IReadOnlyList<ExamImportOptionDto>? Topics { get; set; }

        /// <summary>The caller owns the exam (not shown to the AI).</summary>
        [JsonIgnore]
        public bool Owner { get; set; }

        /// <summary>gama-api's exam status; 6 is a draft (not shown to the AI).</summary>
        [JsonIgnore]
        public int Status { get; set; }
    }
}
