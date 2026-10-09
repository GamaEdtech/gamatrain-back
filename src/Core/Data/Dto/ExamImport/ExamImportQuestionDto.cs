namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>One question as the AI extracted it from the paper (see <c>ExamImportQuestionViewModel</c> for each field).</summary>
    public sealed class ExamImportQuestionDto
    {
        public long? Id { get; set; }

        public required string Number { get; set; }

        public required string Type { get; set; }

        public string? Text { get; set; }

        public IReadOnlyList<string>? Options { get; set; }

        public string? Correct { get; set; }

        public string? Answer { get; set; }

        public string? AnswerSource { get; set; }

        public int? Marks { get; set; }

        public int? TopicId { get; set; }

        public int? Level { get; set; }

        public bool NeedsFigure { get; set; }

        public string? Figure { get; set; }

        public IReadOnlyList<string>? OptionFigures { get; set; }

        public string? AnswerFigure { get; set; }

        public IReadOnlyList<string>? ReviewNotes { get; set; }

        public bool Skip { get; set; }
    }
}
