namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What a "report a problem" is about (<c>ContentReports.ContentType</c>, 2026-11-23). The values are stored: never
    /// renumber them. <see cref="FromLegacyName"/> reads the names the website sent gama-api's <c>reports</c>.
    /// </summary>
    public sealed class ReportedContentType : Enumeration<ReportedContentType, byte>
    {
        [Display]
        public static readonly ReportedContentType Paper = new(nameof(Paper), 1);

        [Display]
        public static readonly ReportedContentType Exam = new(nameof(Exam), 2);

        /// <summary>One question of an exam (from the exam result's answers).</summary>
        [Display]
        public static readonly ReportedContentType ExamQuestion = new(nameof(ExamQuestion), 3);

        [Display]
        public static readonly ReportedContentType Tutorial = new(nameof(Tutorial), 4);

        [Display]
        public static readonly ReportedContentType Multimedia = new(nameof(Multimedia), 5);

        /// <summary>A Q&amp;A question.</summary>
        [Display]
        public static readonly ReportedContentType Question = new(nameof(Question), 6);

        public ReportedContentType()
        {
        }

        public ReportedContentType(string name, byte value)
            : base(name, value)
        {
        }

        /// <summary>Our name or number, or one of the names the website sent gama-api (<c>test</c> a paper, <c>examTest</c> an
        /// exam's question, <c>azmoon</c> an exam, <c>learnfile</c> a multimedia file, <c>question</c> a Q&amp;A).</summary>
        public static ReportedContentType? FromLegacyName(string? name) => name?.Trim().ToUpperInvariant() switch
        {
            null or "" => null,
            "TEST" or "PAPER" => Paper,
            "AZMOON" or "EXAM" => Exam,
            "EXAMTEST" or "EXAMQUESTION" => ExamQuestion,
            "TUTORIAL" or "DARS" => Tutorial,
            "LEARNFILE" or "MULTIMEDIA" or "FILE" => Multimedia,
            "QUESTION" or "QA" => Question,
            _ => name.TryGetFromNameOrValue<ReportedContentType, byte>(out var parsed) ? parsed : null,
        };
    }
}
