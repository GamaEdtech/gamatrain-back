namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using System.ComponentModel;
    using System.Text.Json.Serialization;

    /// <summary>A file the user attached in ChatGPT, as ChatGPT fills an <c>openai/fileParams</c> tool parameter.</summary>
    public sealed class McpFileViewModel
    {
        [JsonPropertyName("download_url")]
        [Description("A short-lived link to the file.")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("file_id")]
        public string? FileId { get; set; }

        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }
}
