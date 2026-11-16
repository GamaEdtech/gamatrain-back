namespace GamaEdtech.Infrastructure.Provider.File
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Azure.Storage.Blobs;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.Provider.File;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class AzureFileProvider(Lazy<ILogger<AzureFileProvider>> logger, Lazy<IConfiguration> configuration) : FileProviderBase(configuration)
    {
        public override FileProviderType ProviderType => FileProviderType.Azure;

        public override async Task<ResultData<Uri?>> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto)
        {
            try
            {
                var url = GetClient(requestDto.ContainerType, requestDto.FileId!)
                    .GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read,
                        DateTimeOffset.UtcNow.Add(requestDto.ContainerType.IsPrivate() ? PrivateUrlLifetime : TimeSpan.FromMinutes(10)));

                return new(OperationResult.Succeeded) { Data = await Task.FromResult(url) };
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

                _ = await GetClient(requestDto.ContainerType, name).UploadAsync(new BinaryData(requestDto.File));

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
                _ = await GetClient(requestDto.ContainerType, requestDto.FileId)
                    .DeleteIfExistsAsync(Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private BlobClient GetClient([NotNull] ContainerType containerType, string fileId)
        {
            var connection = Configuration.Value.GetValue<string>("FileProvider:Azure:ConnectionString");

            return new BlobClient(connection, GenerateBlobContainerName(containerType), fileId);
        }
    }
}
