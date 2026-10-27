namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    public sealed class PrepareExamAnalysisRequestDto
    {
        public required long UserId { get; set; }
        public required long ExamId { get; set; }

        /// <summary>The caller's gama-api token: the exam result (questions, answers, score) is read from gama-api with
        /// it, never taken from the client.</summary>
        public required string SecretKey { get; set; }
    }
}
