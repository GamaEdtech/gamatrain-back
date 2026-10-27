namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using System.Text.Json.Nodes;

    /// <summary>The exam results page's data (<c>GET exams/{id}/result</c>).</summary>
    public sealed class ExamResultPageResponseViewModel
    {
        /// <summary>gama-api's <c>exams/result/{id}</c> data, unchanged: its keys stay snake_case.</summary>
        public JsonObject? Result { get; set; }

        /// <summary>The caller's AI analysis of this result, or what getting one costs. Null when it couldn't be read.</summary>
        public ExamAnalysisStatusResponseViewModel? Analysis { get; set; }
    }
}
