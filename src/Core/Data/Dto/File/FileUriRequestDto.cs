namespace GamaEdtech.Data.Dto.File
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class FileUriRequestDto
    {
        public required string? FileId { get; set; }
        public required ContainerType ContainerType { get; set; }

        /// <summary>Optional file name the browser saves the file under (a private file's signed URL only).</summary>
        public string? DownloadName { get; set; }
    }
}
