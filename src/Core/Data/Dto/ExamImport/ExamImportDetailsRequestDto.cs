namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;

    /// <summary>The <c>set_exam_details</c> MCP tool's input: only the given fields change; the ids come from <c>list_options</c>
    /// and are checked against gama-api.</summary>
    public sealed class ExamImportDetailsRequestDto
    {
        [Description("Board id (list_options kind=board), e.g. Cambridge.")]
        public int? BoardId { get; set; }

        [Description("Grade id under the board (list_options kind=grade), e.g. IGCSE.")]
        public int? GradeId { get; set; }

        [Description("Course id, only for boards that have courses (list_options kind=course). 0 clears it.")]
        public int? CourseId { get; set; }

        [Description("Subject id under the grade (list_options kind=subject).")]
        public int? SubjectId { get; set; }

        [Description("Paper type id (list_options kind=paper), e.g. Paper 2.")]
        public int? PaperId { get; set; }

        [Description("Syllabus/component code, e.g. 9709/12. An empty string clears it.")]
        public string? Component { get; set; }

        [Description("3 = February/March, 6 = May/June, 11 = October/November (any 1-12).")]
        [Range(1, 12)]
        public int? SessionMonth { get; set; }

        [Description("The paper's year.")]
        public int? Year { get; set; }

        [Description("Duration in minutes.")]
        [Range(1, 600)]
        public int? DurationMinutes { get; set; }

        [Description("Exam title. Empty uses the default title (subject, component, session).")]
        public string? Title { get; set; }

        [Description("1 easy, 2 medium, 3 hard; 0 clears it.")]
        [Range(0, 3)]
        public int? Level { get; set; }

        [Description("Wrong answers lose marks.")]
        public bool? NegativeMarking { get; set; }

        [Description("Links the exam to this past paper (from find_past_papers). 0 unlinks.")]
        public long? PastPaperId { get; set; }
    }
}
