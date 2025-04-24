namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    public interface IFileUploader
    {
        string UploaderProviderName { get; }
        ValueTask<List<FileResponse>> GetFilesUrlAsync(string[] fileAddresses, string bucketName);
        Task<UploadFileResponse> UploadFileAsync(UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken);
    }

    public abstract class BaseFileUploader(string connectionString, Dictionary<string, string> containers) : IFileUploader
    {
        public abstract string UploaderProviderName { get; }
        public string ConnectionString { get; } = connectionString;

        public abstract ValueTask<List<FileResponse>> GetFilesUrlAsync(string[] fileAddresses, string bucketName);

        public abstract Task<UploadFileResponse> UploadFileAsync(UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken);

        public string FindContainer(string bucketName) =>
        containers.TryGetValue(bucketName, out var container) ? container : string.Empty;
    }
}
