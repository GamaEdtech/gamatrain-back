namespace GamaEdtech.Domain.DataAccess.Requests.MediaRequests
{
    public class UploadFileRequest
    {
        public IEnumerable<FileRequest> Files { get; init; }
    }

    public class FileRequest
    {
        public string FileName { get; init; }
        public byte[] FileDate { get; init; }
        public string ContentType { get; init; }
    }
}
