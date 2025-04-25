namespace GamaEdtech.Presentation.Api.Configuration
{
    public sealed class AppSetting
    {
        public ConnectionStrings ConnectionStrings { get; init; }
        public FileUploadConfig FileUpload { get; init; }
    }
    public sealed class ConnectionStrings
    {
        public required string SqlServer { get; init; }
        public required string Azure { get; init; }
    }

    public sealed class FileUploadConfig
    {
        public Dictionary<string, Uploader> Uploaders { get; set; } = [];
    }

    public sealed class Uploader
    {
        public Dictionary<string, string> ContainerNames { get; init; }
        public string? Path { get; set; }
    }
    public sealed class ContainerObject
    {
        public string ContainerName { get; set; }
    }
}
