namespace GamaEdtech.Infrastructure.Provider.File
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using Amazon.S3;
    using Amazon.S3.Model;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.Provider.File;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class AmazonS3FileProvider(Lazy<ILogger<AmazonS3FileProvider>> logger, Lazy<IConfiguration> configuration) : FileProviderBase(configuration)
    {
        public override FileProviderType ProviderType => FileProviderType.AmazonS3;

        public override async Task<ResultData<Uri?>> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto)
        {
            try
            {
                var key = $"{GenerateBlobContainerName(requestDto.ContainerType)}/{requestDto.FileId}";
                GetPreSignedUrlRequest request = new()
                {
                    BucketName = BucketFor(requestDto.ContainerType),
                    Key = key,
                    Expires = requestDto.ContainerType.IsPrivate() ? DateTime.UtcNow.Add(PrivateUrlLifetime) : DateTime.Now.AddMinutes(10),
                };
                if (requestDto.ContainerType.IsPrivate() && !string.IsNullOrWhiteSpace(requestDto.DownloadName))
                {
                    request.ResponseHeaderOverrides.ContentDisposition = ContentDisposition(requestDto.DownloadName);
                }

                var url = await Client.GetPreSignedURLAsync(request);

                return new(OperationResult.Succeeded) { Data = new Uri(url) };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public override async Task<ResultData<string?>> UploadFileAsync([NotNull] UploadFileRequestDto requestDto)
        {
            try
            {
                var name = GenerateBlobFileName(requestDto.FileExtension);
                var key = $"{GenerateBlobContainerName(requestDto.ContainerType)}/{name}";
                var bucketName = BucketFor(requestDto.ContainerType);

                InitiateMultipartUploadRequest initiateRequest = new()
                {
                    BucketName = bucketName,
                    Key = key,
                    ContentType = requestDto.ContentType,
                };
                var initResponse = await Client.InitiateMultipartUploadAsync(initiateRequest);
                var contentLength = requestDto.File.LongLength;
                var partSize = 5 * (long)Math.Pow(2, 20); // 5 MB
                long filePosition = 0;
                using var stream = new MemoryStream(requestDto.File);
                List<UploadPartResponse> uploadResponses = [];
                var partNumber = 1;
                while (filePosition < contentLength)
                {
                    UploadPartRequest uploadRequest = new()
                    {
                        BucketName = bucketName,
                        Key = key,
                        UploadId = initResponse.UploadId,
                        PartNumber = partNumber,
                        PartSize = partSize,
                        FilePosition = filePosition,
                        InputStream = stream,
                        DisablePayloadSigning = true,
                    };

                    // Upload a part and add the response to our list.
                    uploadResponses.Add(await Client.UploadPartAsync(uploadRequest));

                    filePosition += partSize;
                    partNumber++;
                }
                CompleteMultipartUploadRequest completeRequest = new()
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = initResponse.UploadId
                };
                completeRequest.AddPartETags(uploadResponses);

                _ = await Client.CompleteMultipartUploadAsync(completeRequest);

                return new(OperationResult.Succeeded) { Data = name };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public override async Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto)
        {
            try
            {
                if (string.IsNullOrEmpty(requestDto.FileId))
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var key = $"{GenerateBlobContainerName(requestDto.ContainerType)}/{requestDto.FileId}";
                _ = await Client.DeleteObjectAsync(BucketFor(requestDto.ContainerType), key);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private string BucketName => Configuration.Value.GetValue<string>("FileProvider:AmazonS3:BucketName")!;

        /// <summary>
        /// The bucket a container's files live in. A private container uses <c>FileProvider:AmazonS3:PrivateBucketName</c>
        /// and never falls back to the public bucket: without it, the call fails.
        /// </summary>
        private string BucketFor(ContainerType containerType)
        {
            if (!containerType.IsPrivate())
            {
                return BucketName;
            }

            var privateBucket = Configuration.Value.GetValue<string?>("FileProvider:AmazonS3:PrivateBucketName");
            return string.IsNullOrWhiteSpace(privateBucket)
                ? throw new InvalidOperationException("FileProvider:AmazonS3:PrivateBucketName is not set; private files are never stored in the public bucket.")
                : privateBucket;
        }

        /// <summary>An attachment header with the name as typed (RFC 5987) and a plain ASCII fallback.</summary>
        private static string ContentDisposition(string downloadName)
        {
            var ascii = new string([.. downloadName.Select(t => t is >= ' ' and <= '~' and not '"' and not '\\' ? t : '_')]);
            return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(downloadName)}";
        }

        private AmazonS3Client Client
        {
            get
            {
                var serviceUrl = Configuration.Value.GetValue<string>("FileProvider:AmazonS3:ServiceURL");
                var secretAccessKey = Configuration.Value.GetValue<string>("FileProvider:AmazonS3:SecretAccessKey");
                var accessKeyId = Configuration.Value.GetValue<string>("FileProvider:AmazonS3:AccessKeyId");

                return new AmazonS3Client(accessKeyId, secretAccessKey, new AmazonS3Config
                {
                    ServiceURL = serviceUrl,
                    UseHttp = true,
                    ForcePathStyle = true,
                    AllowAutoRedirect = true,
                });
            }
        }
    }
}
