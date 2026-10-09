namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// The <c>set_exam_details</c> MCP tool's input: every detail of the exam at once (a field left out is unset), so
    /// changing the board, grade or course never keeps a subject from the earlier choice. The ids come from
    /// <c>list_options</c> and are checked against gama-api as a chain.
    /// </summary>
    public sealed class ExamImportDetailsRequestDto
    {
        [Description("The draft exam to change (examId from an earlier set_exam_details or session_status). Leave it out to create the draft.")]
        public long? ExamId { get; set; }

        [Description("Board id (list_options kind=board), e.g. Cambridge.")]
        public required int BoardId { get; set; }

        [Description("Grade id under the board (list_options kind=grade), e.g. IGCSE.")]
        public required int GradeId { get; set; }

        [Description("Course id, required for boards that have courses (list_options kind=course).")]
        public int? CourseId { get; set; }

        [Description("Subject id under the grade (list_options kind=subject).")]
        public required int SubjectId { get; set; }

        [Description("Paper type id (list_options kind=paper), e.g. Paper 2.")]
        public required int PaperId { get; set; }

        [Description("Duration in minutes.")]
        [Range(1, 600)]
        public required int DurationMinutes { get; set; }

        [Description("Syllabus/component code, e.g. 9709/12; it goes into the default title.")]
        public string? Component { get; set; }

        [Description("3 = February/March, 6 = May/June, 11 = October/November (any 1-12).")]
        [Range(1, 12)]
        public int? SessionMonth { get; set; }

        [Description("The paper's year.")]
        public int? Year { get; set; }

        [Description("Exam title. Leave it out for the default title (subject, component, session).")]
        public string? Title { get; set; }

        [Description("1 easy, 2 medium, 3 hard.")]
        [Range(1, 3)]
        public int? Level { get; set; }

        [Description("Wrong answers lose marks.")]
        public bool NegativeMarking { get; set; }

        [Description("Links the exam to this past paper (from find_past_papers or load_paper) when the draft is created.")]
        public long? PastPaperId { get; set; }
    }
}
