namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class FileService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<FileService>> localizer
        , Lazy<ILogger<FileService>> logger, Lazy<IConfiguration> configuration, Lazy<IGenericFactory<IFileProvider, FileProviderType>> genericFactory)
        : LocalizableServiceBase<FileService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IFileService
    {
        private IFileProvider FileProvider
        {
            get
            {
                _ = configuration.Value.GetValue<string?>("FileProvider:Type").TryGetFromNameOrValue<FileProviderType, byte>(out var fileProviderType);
                return genericFactory.Value.GetProvider(fileProviderType!)!;
            }
        }

        public string? GetStaticFileUrl([NotNull] FileUriRequestDto requestDto)
        {
            try
            {
                return string.IsNullOrEmpty(requestDto.FileId)
                    ? null
                    : FileProvider.GetStaticFileUrl(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        public async Task<Uri?> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto)
        {
            try
            {
                if (string.IsNullOrEmpty(requestDto.FileId))
                {
                    return null;
                }

                var result = await FileProvider.GetFileUrlAsync(requestDto);
                return result.Data;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        public async Task<ResultData<string?>> CreateFileAsync([NotNull] CreateFileRequestDto requestDto)
        {
            try
            {
                using MemoryStream stream = new();
                await requestDto.File.CopyToAsync(stream);

                return await FileProvider.UploadFileAsync(new()
                {
                    File = stream.ToArray(),
                    ContainerType = requestDto.ContainerType,
                    FileExtension = Path.GetExtension(requestDto.File.FileName),
                    ContentType = requestDto.File.ContentType,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto)
        {
            try
            {
                return string.IsNullOrEmpty(requestDto.FileId)
                    ? new(OperationResult.Succeeded) { Data = true }
                    : await FileProvider.RemoveFileAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<ListFilesResponseDto>> ListFilesAsync([NotNull] ListFilesRequestDto requestDto)
        {
            try
            {
                return await FileProvider.ListFilesAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}
