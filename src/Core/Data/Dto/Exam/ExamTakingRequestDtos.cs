namespace GamaEdtech.Data.Dto.Exam
{
    /// <summary>Starting (or resuming) an exam: gama-api's <c>exams/start/{id}</c>.</summary>
    public sealed class ExamStartRequestDto
    {
        public required long ExamId { get; set; }

        /// <summary>The student's gama-api token (the legacy source).</summary>
        public required string? SecretKey { get; set; }

        /// <summary>The student here (the native source).</summary>
        public long? UserId { get; set; }
    }

    /// <summary>A caller's list: their results (<c>exams/results</c>) or, with <see cref="ExamId"/>, their exam's participants
    /// (<c>exams/participants/{id}</c>), with gama-api's query (<c>page</c>, <c>perpage</c>...).</summary>
    public sealed class ExamListRequestDto
    {
        public long? ExamId { get; set; }

        public required string? SecretKey { get; set; }

        public long? UserId { get; set; }

        public string? Query { get; set; }
    }
}
