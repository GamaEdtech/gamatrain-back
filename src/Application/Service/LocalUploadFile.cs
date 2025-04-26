namespace GamaEdtech.Application.Service
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.RegularExpressions;

    public partial class LocalUploadFile(string rootDirectory, Uri baseUrl, bool isEnable) : BaseFileUploader(rootDirectory ?? string.Empty, [], isEnable)
    {
        public override string UploaderProviderName => "Local";

        public override ValueTask<List<FileResponse>> GetFilesUrlAsync([NotNull] string[] fileAddresses, string bucketName)
        {
            var files = new List<FileResponse>();

            foreach (var fileAddress in fileAddresses)
            {
                var filePath = Path.Combine(rootDirectory, bucketName, fileAddress);
                if (File.Exists(filePath))
                {
                    files.Add(new FileResponse
                    {
                        ContentType = string.Empty,
                        FileAddress = fileAddress,
                        Url = $"/{bucketName}/{fileAddress}"
                    });
                }
            }

            return ValueTask.FromResult(files);
        }

        public override async Task<UploadFileResponse> UploadFileAsync([NotNull] UploadFileRequest uploadFileRequest, string bucketName, CancellationToken cancellationToken)
        {
            try
            {
                var targetDirectory = Path.Combine(rootDirectory, bucketName);
                var sanitizedDirectory = ExtractRootDirectory().Replace(rootDirectory, string.Empty);

                if (!Directory.Exists(targetDirectory))
                {
                    _ = Directory.CreateDirectory(targetDirectory);
                }

                var files = new ConcurrentBag<FileResponse>();

                var fileUploadTasks = uploadFileRequest.Files.Select(file =>
                {
                    var filePath = Path.Combine(targetDirectory, file.FileName);
                    return (file.ContentType, file.FileName, UploadTask: File.WriteAllBytesAsync(filePath, file.FileDate, cancellationToken));
                });

                foreach (var (contentType, fileName, uploadTask) in fileUploadTasks)
                {
                    try
                    {
                        await uploadTask;
                        files.Add(new FileResponse
                        {
                            ContentType = contentType,
                            FileName = fileName,
                            Url = $"{baseUrl}/{sanitizedDirectory}/{bucketName}/{fileName}",
                            FileUploadStatus = true,
                            FileAddress = $"{baseUrl}/{sanitizedDirectory}/{bucketName}/{fileName}",
                        });
                    }
                    catch
                    {
                        throw;
                    }
                }

                return new UploadFileResponse { FileResults = files.ToList() };
            }
            catch
            {
                return new UploadFileResponse { FileResults = new List<FileResponse>() };
            }
        }

        [GeneratedRegex(@"^wwwroot[/\\]")]
        private static partial Regex ExtractRootDirectory();
    }
}
