namespace GamaEdtech.Application.Service
{
    using Azure.Storage.Blobs;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;

    public class AzureUploadFile(string? connectionString, Dictionary<string, string> containers)
        : BaseFileUploader(connectionString ?? string.Empty, containers)
    {
        public override string UploaderProviderName => "Azure";


        public override ValueTask<List<FileResponse>> GetFilesUrlAsync(string[] fileAddresses, string bucketName)
        {
            var files = new List<FileResponse>();

            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(bucketName);

            var fileUrlTasks = fileAddresses.Select(fileAddress => (fileAddress, UrlFunc: containerClient.GenerateSasUri
                (Azure.Storage.Sas.BlobContainerSasPermissions.Read,
                DateTimeOffset.UtcNow.AddMinutes(10))));

            foreach ((var fileAddress, var urlFunc) in fileUrlTasks)
            {
                var uri = urlFunc;
                files.Add(new FileResponse
                {
                    ContentType = string.Empty,
                    FileAddress = fileAddress,
                    Url = uri.ToString()
                });
            }
            return ValueTask.FromResult(files);
        }

        public override async Task<UploadFileResponse> UploadFileAsync([NotNull] UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken)
        {
            try
            {
                var containerName = FindContainer(bucketName);
                if (string.IsNullOrEmpty(containerName))
                {
                    throw new ArgumentException("");
                }

                var files = new ConcurrentBag<FileResponse>();

                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                _ = await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                var fileUploadTasks = uploadFileRequest.Files.Select(file =>
                {
                    var blobClient = containerClient.GetBlobClient(file.FileName);
                    return (file.ContentType, file.FileName, UploadTask:
                    blobClient.UploadAsync(new MemoryStream(file.FileDate), overwrite: true, cancellationToken));
                });

                foreach (var (contentType, fileName, uploadTask) in fileUploadTasks)
                {
                    var uploadResult = await uploadTask;
                    if (uploadResult.GetRawResponse().Status == 200)
                    {
                        files.Add(new FileResponse
                        {
                            ContentType = contentType,
                            FileName = fileName,
                        });
                    }
                }

                return new UploadFileResponse { FileResults = [.. files] };
            }
            catch
            {
                return new UploadFileResponse() { FileResults = [] };
            }
        }
    }
}
