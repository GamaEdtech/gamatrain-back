namespace GamaEdtech.Application.Service
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// A past paper file's role as the API names it (since 2026-11-25): <c>question</c> (the question paper, PDF, required),
    /// <c>word</c> (the question paper as Word), <c>answer</c> (the mark scheme) and <c>extra</c> (any other file, its kind
    /// in <c>type_id</c>). One role per <see cref="PastPaperFileKind"/>; every file is otherwise named by its own id.
    /// See docs/business/past-papers.md, "Files".
    /// </summary>
    internal static class PastPaperFileRoles
    {
        public const string Question = "question";
        public const string Word = "word";
        public const string Answer = "answer";
        public const string Extra = "extra";

        public static string Of(PastPaperFileKind kind) => kind.Value switch
        {
            1 => Question,
            2 => Answer,
            3 => Word,
            _ => Extra,
        };

        public static PastPaperFileKind? KindOf(string? role) => role switch
        {
            Question => PastPaperFileKind.Question,
            Word => PastPaperFileKind.QuestionWord,
            Answer => PastPaperFileKind.Answer,
            Extra => PastPaperFileKind.Extra,
            _ => null,
        };

        /// <summary>The order files are listed in: question, word, answer, extra.</summary>
        public static int Order(PastPaperFileKind kind) => kind.Value switch
        {
            1 => 0,
            3 => 1,
            2 => 2,
            _ => 3,
        };
    }
}
