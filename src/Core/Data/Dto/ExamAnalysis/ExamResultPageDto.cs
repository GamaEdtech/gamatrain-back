namespace GamaEdtech.Data.Dto.ExamAnalysis
{
    using System.Text.Json.Nodes;

    /// <summary>What the exam results page shows (<c>GET exams/{id}/result</c>): the caller's gama-api result as gama-api
    /// sends it, plus their AI analysis or what one costs, in one call.</summary>
    public sealed class ExamResultPageDto
    {
        /// <summary>gama-api's <c>exams/result/{id}</c> <c>data</c>, unchanged (snake_case keys), so a field gama-api adds
        /// reaches the page without a change here.</summary>
        public JsonObject? Result { get; set; }

        /// <summary><see langword="null"/> when it couldn't be read: the page still shows the result.</summary>
        public ExamAnalysisStatusDto? Analysis { get; set; }
    }
}
