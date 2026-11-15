namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary><c>POST exams/{id}/end</c>: the exam page's submit, as it sends it to gama-api, plus the countdown's time spent.</summary>
    public sealed class ExamEndRequestViewModel
    {
        /// <summary>gama-api's attempt id from <c>exams/start</c>, passed through unchanged.</summary>
        [JsonPropertyName("startID")]
        public JsonElement? StartId { get; set; }

        /// <summary>The answers, passed through to gama-api unchanged.</summary>
        public JsonElement? Answers { get; set; }

        /// <summary>Seconds the exam's countdown ran (its duration minus what was left); at most the duration is kept.</summary>
        public int? TimeSpentSeconds { get; set; }
    }

    public sealed class ExamEndResponseViewModel
    {
        /// <summary>gama-api's result id: the result page to open. Also set when gama-api refuses an exam already submitted.</summary>
        public long? Id { get; set; }
    }
}
