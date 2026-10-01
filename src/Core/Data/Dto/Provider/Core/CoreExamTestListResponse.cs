namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    /// <summary>gama-api's <c>GET examTests?exam_id={id}</c> (config <c>Core:ExamTest</c>): a question search, used
    /// with one exam id. Its <c>list</c> holds all of that exam's questions, unpaged (empty for an unknown exam);
    /// callers still match each item by id against <see cref="CoreExamResponse.Tests"/>.</summary>
    public sealed class CoreExamTestListResponse
    {
        [JsonPropertyName("list")]
        public Collection<CoreExamTestResponse>? List { get; set; }
    }
}
