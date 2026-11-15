namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.File;

    [Injectable]
    public interface IFileService
    {
        string? GetStaticFileUrl([NotNull] FileUriRequestDto requestDto);
        Task<Uri?> GetFileUrlAsync([NotNull] FileUriRequestDto requestDto);
        Task<ResultData<string?>> CreateFileAsync([NotNull] CreateFileRequestDto requestDto);
        Task<ResultData<bool>> RemoveFileAsync([NotNull] RemoveFileRequestDto requestDto);

        /// <summary>One page of the files stored in a container (the configured provider), in name order. Reads only.</summary>
        Task<ResultData<ListFilesResponseDto>> ListFilesAsync([NotNull] ListFilesRequestDto requestDto);
    }
}
