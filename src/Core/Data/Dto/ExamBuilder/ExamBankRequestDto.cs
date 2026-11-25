namespace GamaEdtech.Data.Dto.ExamBuilder
{
    /// <summary>The question bank a teacher builds exams from: published questions (and the caller's own) of a board, grade,
    /// subject and topic, a page at a time.</summary>
    public sealed class ExamBankRequestDto
    {
        /// <summary>The caller's gama-api token, for the <c>Legacy</c> source.</summary>
        public string? SecretKey { get; set; }

        /// <summary>The signed-in caller here, which the native source acts for.</summary>
        public long UserId { get; set; }

        public int? BoardId { get; set; }

        public int? GradeId { get; set; }

        public int? SubjectId { get; set; }

        public int? TopicId { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
