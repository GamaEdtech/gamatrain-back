namespace GamaEdtech.Data.Dto.Provider.Core
{
    using System.Collections.ObjectModel;
    using System.Text.Json.Serialization;

    /// <summary>gama-api's <c>GET examTests?id={id}</c> (config <c>Core:ExamTest</c>): a question search, used with
    /// one question id at a time. Its <c>list</c> holds that question -- but an id it doesn't recognize is
    /// ignored and the whole question bank comes back instead, so callers must match the item by id.</summary>
    public sealed class CoreExamTestListResponse
    {
        [JsonPropertyName("list")]
        public Collection<CoreExamTestResponse>? List { get; set; }
    }
}
