namespace GamaEdtech.Infrastructure.Provider.File
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.Provider.File;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;

    public abstract class FileProviderBase(Lazy<IConfiguration> configuration) : IFileProvider
    {
        protected Lazy<IConfiguration> Configuration => configuration;

        public abstract FileProviderType ProviderType { get; }

        /// <summary>The permanent CDN URL of a public file. A private container's file never has one: <c>null</c>.</summary>
        public string? GetStaticFileUrl([NotNull] FileUriRequestDto requestDto) => requestDto.ContainerType.IsPrivate()
            ? null
            : $"{Configuration.Value.GetValue<string>("Core:Cdn")}/{GenerateBlobContainerName(requestDto.ContainerType)}/{requestDto.FileId}";

        public abstract Task<ResultData<Uri?>> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto);

        public abstract Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto);

        public abstract Task<ResultData<string?>> UploadFileAsync([NotNull] UploadFileRequestDto requestDto);

        public abstract Task<ResultData<ListFilesResponseDto>> ListFilesAsync([NotNull] ListFilesRequestDto requestDto);

        protected static int PageSize([NotNull] ListFilesRequestDto requestDto) => Math.Clamp(requestDto.MaxCount, 1, 1000);

        /// <summary>How long a private file's signed URL works: <c>FileProvider:PrivateUrlLifetimeMinutes</c>, 1 to 60, default 5.</summary>
        protected TimeSpan PrivateUrlLifetime => TimeSpan.FromMinutes(Math.Clamp(Configuration.Value.GetValue("FileProvider:PrivateUrlLifetimeMinutes", 5), 1, 60));

        protected static string GenerateBlobFileName(string? fileExtension) => $"{Guid.NewGuid():N}{fileExtension}";

        protected static string GenerateBlobContainerName([NotNull] ContainerType containerType) => containerType.Name.ToLowerInvariant();
    }
}
