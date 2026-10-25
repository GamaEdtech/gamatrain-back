namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    public sealed class ExamAnalysisQuestionDto
    {
        public required string Question { get; set; }

        public required IReadOnlyList<string> Answers { get; set; }

        public required int UserAnswer { get; set; }

        public required int TrueAnswer { get; set; }
    }
}
