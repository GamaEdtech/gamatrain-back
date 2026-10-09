namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>gama-api's choices for an exam detail.</summary>
    public sealed class ExamImportOptionsRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>board, grade, course, subject, topic or paper.</summary>
        public required string Kind { get; set; }

        /// <summary>The board of a grade or course, the grade of a subject, the subject of a topic.</summary>
        public int? ParentId { get; set; }

        /// <summary>A subject's course, for boards that have courses.</summary>
        public int? CourseId { get; set; }
    }
}
