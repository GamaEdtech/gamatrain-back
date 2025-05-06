namespace GamaEdtech.Infrastructure.Provider.File
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Azure.Storage.Blobs;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class AzureFileProvider(Lazy<ILogger<AzureFileProvider>> logger, Lazy<IConfiguration> configuration) : IFileProvider
    {
        public FileProviderType ProviderType => FileProviderType.Azure;

        public ResultData<Uri?> GetFileUri(string id, ContainerType containerType)
        {
            try
            {
                var url = GetClient(containerType, id)
                    .GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10));

                return new(OperationResult.Succeeded) { Data = url };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<string?>> UploadFileAsync([NotNull] UploadFileRequestDto requestDto)
        {
            try
            {
                var name = $"{Guid.NewGuid():N}{requestDto.FileExtension}";

                _ = await GetClient(requestDto.ContainerType, name).UploadAsync(new BinaryData(requestDto.File));

                return new(OperationResult.Succeeded) { Data = name };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto)
        {
            try
            {
                _ = await GetClient(requestDto.ContainerType, requestDto.FileId)
                    .DeleteIfExistsAsync(Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots);

                return new(OperationResult.Succeeded) { Data = await Task.FromResult(true) };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private BlobClient GetClient([NotNull] ContainerType containerType, string fileId)
        {
            var key = "FileProvider:Azure:ContainerName";
            if (containerType == ContainerType.School)
            {
                key = "FileProvider:Azure:SchoolContainerName";
            }
            else if (containerType == ContainerType.Post)
            {
                key = "FileProvider:Azure:PostContainerName";
            }

            var container = configuration.Value.GetValue<string>(key);
            var connection = configuration.Value.GetValue<string>("FileProvider:Azure:ConnectionString");

            return new BlobClient(connection, container, fileId);
        }
    }
}
