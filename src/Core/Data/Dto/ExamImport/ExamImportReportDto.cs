namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>What <c>save_questions</c> and <c>remove_question</c> did: each question's outcome and its problems.</summary>
    public sealed class ExamImportReportDto
    {
        public const string SavedStatus = "saved";
        public const string ReviewStatus = "review";
        public const string BlockedStatus = "blocked";
        public const string SkippedStatus = "skipped";
        public const string RemovedStatus = "removed";
        public const string FailedStatus = "failed";

        public IReadOnlyList<QuestionDto>? Questions { get; set; }

        /// <summary>How many questions the draft has now.</summary>
        public int DraftQuestions { get; set; }

        public sealed class QuestionDto
        {
            public string? Number { get; set; }

            /// <summary>The question's id on gama-api: pass it with the question to change it later.</summary>
            public long? Id { get; set; }

            /// <summary>saved, review (saved, a human should look), blocked (not saved: fix it), skipped, removed or failed
            /// (gama-api refused it, see <see cref="Error"/>).</summary>
            public string? Status { get; set; }

            public IReadOnlyList<IssueDto>? Issues { get; set; }

            public string? Error { get; set; }
        }

        public sealed class IssueDto
        {
            /// <summary>The answer was written by the AI, not taken from the mark scheme (review).</summary>
            public const string AiAnswerCode = "aiAnswer";

            /// <summary>An open question without an answer (error).</summary>
            public const string MissingAnswerCode = "missingAnswer";

            /// <summary>A choice question without a correct letter (error).</summary>
            public const string MissingCorrectCode = "missingCorrect";

            /// <summary><c>error</c>: cannot be saved until fixed or skipped. <c>review</c>: a human should look.</summary>
            public string? Severity { get; set; }

            public string? Code { get; set; }

            public string? Message { get; set; }
        }
    }
}
