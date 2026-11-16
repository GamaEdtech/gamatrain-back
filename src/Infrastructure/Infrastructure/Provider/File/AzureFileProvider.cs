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

        public override async Task<ResultData<ListFilesResponseDto>> ListFilesAsync([NotNull] ListFilesRequestDto requestDto)
        {
            try
            {
                var connection = Configuration.Value.GetValue<string>("FileProvider:Azure:ConnectionString");
                BlobContainerClient container = new(connection, GenerateBlobContainerName(requestDto.ContainerType));
                var size = PageSize(requestDto);
                List<StoredFileDto> files = [];
                var more = false;
                await foreach (var blob in container.GetBlobsAsync())
                {
                    if (requestDto.StartAfter is not null && string.CompareOrdinal(blob.Name, requestDto.StartAfter) <= 0)
                    {
                        continue;
                    }

                    if (files.Count == size)
                    {
                        more = true;
                        break;
                    }

                    files.Add(new() { Name = blob.Name, Size = blob.Properties.ContentLength ?? 0 });
                }

                return new(OperationResult.Succeeded) { Data = new() { Files = files, NextStartAfter = more ? files[^1].Name : null } };
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
