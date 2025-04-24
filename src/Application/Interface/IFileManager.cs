namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;

    public interface IFileManager
    {
        Task<UploadFileResult> UploadFilesAsync(UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken);
    }
}
