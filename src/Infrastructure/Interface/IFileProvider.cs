namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.Provider.File;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface IFileProvider : IProvider<FileProviderType>
    {
        string? GetStaticFileUrl([NotNull] FileUriRequestDto requestDto);
        Task<ResultData<Uri?>> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto);
        Task<ResultData<string?>> UploadFileAsync([NotNull] UploadFileRequestDto requestDto);
        Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto);

        /// <summary>One page of the files stored in a container, in name order. Reads only.</summary>
        Task<ResultData<ListFilesResponseDto>> ListFilesAsync([NotNull] ListFilesRequestDto requestDto);
    }
}
