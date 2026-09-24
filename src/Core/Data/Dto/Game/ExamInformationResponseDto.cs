namespace GamaEdtech.Data.Dto.Game
{
    public sealed class ExamInformationResponseDto
    {
        public ExamDto? Exam { get; set; }
#pragma warning disable CA1002 // Do not expose generic lists
        public List<TestDto>? Tests { get; set; }
#pragma warning restore CA1002 // Do not expose generic lists
        public string? Url { get; set; }

        public sealed class ExamDto
        {
            public string? Type { get; set; }
            public string? ExamType { get; set; }
            public string? Title { get; set; }
            public int TestsCount { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
            public string? ExamTime { get; set; }

            /// <summary>Difficulty as shown in the header's "Level:" cell: "Easy", "Medium" or "Hard" (gama-api's
            /// <c>level</c> 1/2/3), or the raw value if gama-api ever sends another one.</summary>
            public string? Level { get; set; }

            /// <summary>The exam author's name, shown in the header's "By:" cell -- gama-api's, replaced by our own
            /// user's name when <see cref="AuthorCoreId"/> matches a local account (see ExamSerivce).</summary>
            public string? Author { get; set; }

            /// <summary>The exam author's gama-api user id, matched against our users' <c>CoreId</c>.</summary>
            public long? AuthorCoreId { get; set; }

            public string? QrCode { get; set; }
        }

        public sealed class TestDto
        {
            public string? Question { get; set; }
            public string? QuestionFile { get; set; }
            public string? OptionA { get; set; }
            public string? OptionB { get; set; }
            public string? OptionC { get; set; }
            public string? OptionD { get; set; }
            public string? OptionAFile { get; set; }
            public string? OptionBFile { get; set; }
            public string? OptionCFile { get; set; }
            public string? OptionDFile { get; set; }

            /// <summary>
            /// 'A'/'B'/'C'/'D', whichever option is correct -- from gama-api's per-question <c>true_answer</c>
            /// (1-4), filling the exports' "Answer Key" page. <see langword="null"/> when gama-api gives none
            /// (e.g. a descriptive question), which leaves that row unmarked.
            /// </summary>
            public char? CorrectOption { get; set; }

            /// <summary>
            /// Core's own per-test "type" ("fourchoice"/"descriptive", confirmed live against exams 831/832/
            /// 1061/2037) -- authoritative replacement for guessing MCQ-vs-descriptive from blank option
            /// fields. <see cref="HasOptions"/> prefers this and only falls back to the old blank-field
            /// heuristic when it's null/unrecognized (an older Core deployment, or a value not yet seen).
            /// </summary>
            public string? QuestionType { get; set; }

            /// <summary>
            /// Core's own per-test options-layout hint ("answer_view_type"). Confirmed live across 64 real
            /// questions (exams 831/832/1061/2037): only ever "1"/"2"/"4", cross-referenced against real
            /// option text lengths strongly suggests it's the options column count -- "4" options across one
            /// row, "2" per row, "1" per row (stacked). Never seen a value implying an image-specific layout;
            /// <see cref="TestImageAnswers"/> and <see cref="TestDto.QuestionFile"/> are the independent real
            /// signals for that (see <c>ExamWordDocumentBuilder.ClassifyLayout</c>).
            /// </summary>
            public string? AnswerViewType { get; set; }

            /// <summary>
            /// Core's own "are all four options images, not text" flag ("testImgAnswers") -- authoritative
            /// replacement for guessing it from every option's text being blank.
            /// </summary>
            public bool TestImageAnswers { get; set; }

            /// <summary>
            /// Descriptive-type tests (see exam 831) have all four options blank -- skip the MCQ grid for
            /// those. Prefers <see cref="QuestionType"/> ("fourchoice"/"descriptive") when it's a recognized
            /// value; falls back to checking the option text/File fields directly otherwise (an image-only
            /// option, i.e. a diagram-based MCQ, has no OptionX text at all but is still a real option, not a
            /// descriptive question).
            /// </summary>
            public bool HasOptions => QuestionType switch
            {
                "fourchoice" => true,
                "descriptive" => false,
                _ =>
                    !string.IsNullOrWhiteSpace(OptionA) || !string.IsNullOrWhiteSpace(OptionB) ||
                    !string.IsNullOrWhiteSpace(OptionC) || !string.IsNullOrWhiteSpace(OptionD) ||
                    !string.IsNullOrWhiteSpace(OptionAFile) || !string.IsNullOrWhiteSpace(OptionBFile) ||
                    !string.IsNullOrWhiteSpace(OptionCFile) || !string.IsNullOrWhiteSpace(OptionDFile),
            };
        }
    }
}
