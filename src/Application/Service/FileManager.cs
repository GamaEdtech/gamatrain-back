namespace GamaEdtech.Application.Service
{
    using System.Collections.Concurrent;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;

    public class FileManager(IEnumerable<IFileUploader> fileUploaders, string[] uploaderNames) : IFileManager
    {
        public async Task<UploadFileResult> UploadFilesAsync(UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken)
        {
            var uploadFileResponse = new UploadFileResult();
            var concurrentUploadResponsePerProvider = new ConcurrentBag<UploadResponsePerProvider>();

            var uploaderTasks = fileUploaders.Where(c => uploaderNames.Any(a => a == c.UploaderProviderName))
            .Select(s => (s.UploaderProviderName, UploadTask: s.UploadFileAsync(uploadFileRequest, bucketName, cancellationToken)));

            foreach (var (uploaderName, uploadTask) in uploaderTasks)
            {
                var result = await uploadTask;
                concurrentUploadResponsePerProvider.Add(new UploadResponsePerProvider
                {
                    UploadFileResult = result,
                    UploaderName = uploaderName
                });
            }
            var list = uploadFileResponse.UploadResponsePerProviders.ToList();
            list.AddRange([.. concurrentUploadResponsePerProvider]);
            uploadFileResponse.UploadResponsePerProviders = list;
            return uploadFileResponse;
        }
    }
}
