namespace GamaEdtech.Data.Dto.ExamAdmin
{
    /// <summary>The admin exam list's filters, all optional. Statuses are gama-api's codes (0 waiting, 1 published, 2 rejected,
    /// 3 needs changes, 5 cloned, 6 draft, 7 published by its teacher).</summary>
    public sealed class ExamAdminListRequestDto
    {
        public byte? Status { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public int? ExamTypeId { get; set; }

        public long? OwnerUserId { get; set; }

        /// <summary>The title or code contains it, or it is the id.</summary>
        public string? Text { get; set; }

        public bool IncludeDeleted { get; set; }

        /// <summary>gama-api's question combination: 1 from albums (other teachers' questions), 2 the owner's own, 3 mixed.</summary>
        public byte? Combination { get; set; }

        /// <summary>Whether it can be taken now: true = no dates, or now within its start and end; false = not now.</summary>
        public bool? Open { get; set; }

        public int? Year { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class ExamAdminListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<ExamAdminItemDto> Items { get; set; } = [];
    }

    public class ExamAdminItemDto
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        public string? Title { get; set; }

        public byte Status { get; set; }

        public byte Type { get; set; }

        public bool IsDeleted { get; set; }

        public int? BoardId { get; set; }

        public string? BoardTitle { get; set; }

        public int? GradeId { get; set; }

        public string? GradeTitle { get; set; }

        public int? SubjectId { get; set; }

        public string? SubjectTitle { get; set; }

        public int? ExamTypeId { get; set; }

        public string? ExamTypeTitle { get; set; }

        public int QuestionCount { get; set; }

        public int? DurationMinutes { get; set; }

        public long? OwnerUserId { get; set; }

        public string? OwnerName { get; set; }

        public int ParticipantCount { get; set; }

        public int ViewCount { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? LastModifyDate { get; set; }
    }

    /// <summary>One exam for the admin: the list's fields, its settings and its questions in order.</summary>
    public sealed class ExamAdminDto : ExamAdminItemDto
    {
        public string? CoreTopicIds { get; set; }

        public byte? Level { get; set; }

        public byte? ScoreType { get; set; }

        public int? EduYear { get; set; }

        public byte? EduMonth { get; set; }

        public DateTimeOffset? StartDate { get; set; }

        public DateTimeOffset? EndDate { get; set; }

        public IReadOnlyList<ExamQuestionAdminItemDto> Questions { get; set; } = [];
    }

    /// <summary>An exam's settings as the admin saves them; the question list is set on its own.</summary>
    public sealed class ExamEditDto
    {
        public string? Title { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public string? CoreTopicIds { get; set; }

        public int? ExamTypeId { get; set; }

        public byte? Level { get; set; }

        public int? DurationMinutes { get; set; }

        /// <summary>1 normal, 2 negative marking (a wrong answer takes a third of a right one).</summary>
        public byte? ScoreType { get; set; }

        public int? EduYear { get; set; }

        public byte? EduMonth { get; set; }

        public DateTimeOffset? StartDate { get; set; }

        public DateTimeOffset? EndDate { get; set; }
    }

    /// <summary>The admin question bank's filters, all optional. Statuses: 0 waiting, 1 confirmed, 2 rejected.</summary>
    public sealed class ExamQuestionAdminListRequestDto
    {
        public byte? Status { get; set; }

        public int? BoardId { get; set; }

        public int? SubjectId { get; set; }

        public int? TopicId { get; set; }

        public byte? Type { get; set; }

        public long? OwnerUserId { get; set; }

        /// <summary>The question's text or code contains it, or it is the id.</summary>
        public string? Text { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class ExamQuestionAdminListDto
    {
        public int Total { get; set; }

        public IReadOnlyList<ExamQuestionAdminItemDto> Items { get; set; } = [];
    }

    public class ExamQuestionAdminItemDto
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        /// <summary>The question's text without tags, cut to 200 characters.</summary>
        public string? Excerpt { get; set; }

        public string? Type { get; set; }

        public byte? TrueAnswer { get; set; }

        public byte Status { get; set; }

        public int? SubjectId { get; set; }

        public string? SubjectTitle { get; set; }

        public string? TopicTitle { get; set; }

        public long? OwnerUserId { get; set; }

        public string? OwnerName { get; set; }

        public int UsedPublish { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset? LastModifyDate { get; set; }
    }

    /// <summary>One question for the edit form: its HTML and image links.</summary>
    public sealed class ExamQuestionAdminDto : ExamQuestionAdminItemDto
    {
        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public string? CoreTopicIds { get; set; }

        public byte? Level { get; set; }

        public byte? TypeCode { get; set; }

        public string? Question { get; set; }

        public string? AnswerA { get; set; }

        public string? AnswerB { get; set; }

        public string? AnswerC { get; set; }

        public string? AnswerD { get; set; }

        public string? AnswerFull { get; set; }

        public string? QuestionImage { get; set; }

        public string? AnswerAImage { get; set; }

        public string? AnswerBImage { get; set; }

        public string? AnswerCImage { get; set; }

        public string? AnswerDImage { get; set; }

        public string? AnswerFullImage { get; set; }

        public string? Resource { get; set; }
    }

    /// <summary>A question as the admin saves it. The HTML is sanitized; images go into the HTML through the editor upload.</summary>
    public sealed class ExamQuestionEditDto
    {
        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public string? CoreTopicIds { get; set; }

        public byte? Level { get; set; }

        /// <summary>gama-api's type code (1 four-choice, 2 descriptive, 4 two-choice, 8 true/false, 16 blank, 32 short answer).</summary>
        public byte? Type { get; set; }

        public string? Question { get; set; }

        public string? AnswerA { get; set; }

        public string? AnswerB { get; set; }

        public string? AnswerC { get; set; }

        public string? AnswerD { get; set; }

        public string? AnswerFull { get; set; }

        public byte? TrueAnswer { get; set; }

        public string? Resource { get; set; }

        public byte? Status { get; set; }
    }
}
