namespace GamaEdtech.Data.Mapping.Media
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.DataAccess.Requests.MediaRequests;

    using Microsoft.AspNetCore.Http;

    public static class UploadFileMapping
    {
        public static async Task<UploadFileRequest> MapToUploadFileRequestAsync([NotNull] this IEnumerable<IFormFile> files)
        {
            var fileRequests = new List<FileRequest>();

            foreach (var file in files)
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var fileRequest = new FileRequest
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    FileDate = memoryStream.ToArray()
                };

                fileRequests.Add(fileRequest);
            }

            return new UploadFileRequest { Files = fileRequests };
        }
    }
}
