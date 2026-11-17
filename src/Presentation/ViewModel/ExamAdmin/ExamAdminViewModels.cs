namespace GamaEdtech.Presentation.ViewModel.ExamAdmin
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>An exam's settings as the admin saves them (gama-api type ids); its questions are set on their own.</summary>
    public sealed class ExamEditRequestViewModel
    {
        [Display]
        [Required]
        [StringLength(500)]
        public string? Title { get; set; }

        [Display]
        public int? BoardId { get; set; }

        [Display]
        public int? GradeId { get; set; }

        [Display]
        public int? SubjectId { get; set; }

        /// <summary>Topic ids joined by "+".</summary>
        [Display]
        [StringLength(200)]
        public string? CoreTopicIds { get; set; }

        [Display]
        public int? ExamTypeId { get; set; }

        [Display]
        public byte? Level { get; set; }

        [Display]
        public int? DurationMinutes { get; set; }

        /// <summary>1 normal, 2 negative marking.</summary>
        [Display]
        public byte? ScoreType { get; set; }

        [Display]
        public int? EduYear { get; set; }

        [Display]
        public byte? EduMonth { get; set; }

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }
    }

    /// <summary>gama-api's status code: an exam's 0 waiting, 1 published, 2 rejected, 3 needs changes, 5 cloned, 6 draft, 7
    /// published by its teacher; a question's 0 waiting, 1 confirmed, 2 rejected.</summary>
    public sealed class ExamStatusRequestViewModel
    {
        [Display]
        [Required]
        public byte? Status { get; set; }
    }

    public sealed class ExamQuestionsRequestViewModel
    {
        /// <summary>The exam's questions, in order.</summary>
        [Display]
        [Required]
        public IReadOnlyList<long>? QuestionIds { get; set; }
    }

    /// <summary>A question as the admin saves it: HTML (images through <c>EditorImages</c>), sanitized on save.</summary>
    public sealed class ExamQuestionRequestViewModel
    {
        [Display]
        public int? BoardId { get; set; }

        [Display]
        public int? GradeId { get; set; }

        [Display]
        [Required]
        public int? SubjectId { get; set; }

        [Display]
        [StringLength(100)]
        public string? CoreTopicIds { get; set; }

        [Display]
        public byte? Level { get; set; }

        /// <summary>1 four-choice, 2 descriptive, 4 two-choice, 8 true/false, 16 blank, 32 short answer.</summary>
        [Display]
        [Required]
        public byte? Type { get; set; }

        [Display]
        [Required]
        public string? Question { get; set; }

        [Display]
        public string? AnswerA { get; set; }

        [Display]
        public string? AnswerB { get; set; }

        [Display]
        public string? AnswerC { get; set; }

        [Display]
        public string? AnswerD { get; set; }

        [Display]
        public string? AnswerFull { get; set; }

        /// <summary>The correct option, 1-4.</summary>
        [Display]
        public byte? TrueAnswer { get; set; }

        [Display]
        [StringLength(200)]
        public string? Resource { get; set; }

        [Display]
        public byte? Status { get; set; }
    }
}
