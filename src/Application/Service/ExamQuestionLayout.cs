namespace GamaEdtech.Application.Service
{
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>
    /// How a question's options are laid out (<c>ExamQuestion.AnswerViewType</c>, gama-api's <c>answer_view_type</c>): 4 all
    /// in one row, 2 per row, 1 per row. Worked out on every save from the question itself, as gama-api does
    /// (<c>Examtest_lib::answerViewTypeBasedOnTextLen</c>), so questions made here are laid out like the ones copied from it;
    /// the Word export reads it. Since 2026-11-23. See docs/business/exams-migration.md, "Exam builder API".
    /// </summary>
    internal static partial class ExamQuestionLayout
    {
        // gama-api's type ids (NativeExamSource's QuestionTypes).
        private const byte FourChoice = 1;
        private const byte TwoChoice = 4;
        private const byte TrueFalse = 8;

        /// <summary>
        /// Text options by their longest plain text: under 25 characters 4, under 50 2, else 1 (a two-choice question by
        /// its two options); true/false 4; image options and the open types 1.
        /// </summary>
        public static byte For(byte type, bool imageOptions, IReadOnlyList<string?> options) => type switch
        {
            TrueFalse => 4,
            FourChoice when !imageOptions => ByLength(options.Take(4)),
            TwoChoice when !imageOptions => ByLength(options.Take(2)),
            _ => 1,
        };

        private static byte ByLength(IEnumerable<string?> options) => options.Select(PlainLength).DefaultIfEmpty(0).Max() switch
        {
            < 25 => 4,
            < 50 => 2,
            _ => 1,
        };

        // gama-api's mb_strlen(html_entity_decode(strip_tags(...))): characters of the text, tags left out.
        private static int PlainLength(string? html) => string.IsNullOrEmpty(html)
            ? 0
            : WebUtility.HtmlDecode(Tags().Replace(html, string.Empty)).EnumerateRunes().Count();

        [GeneratedRegex("<[^>]*>")]
        private static partial Regex Tags();
    }
}
