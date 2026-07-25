namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface IContentDeliveryProvider : IProvider<ContentSource>
    {
        Task<ResultData<GetDownloadUrlResponseDto>> GetDownloadUrlAsync([NotNull] GetDownloadUrlRequestDto requestDto);

        /// <summary>Side-effect-free price/paid check for a PastPaper pdf/word/answer file - see GamaApiPaperDetailsResponse for why this exists separately from GetDownloadUrlAsync.</summary>
        Task<ResultData<GetContentPriceStatusResponseDto>> GetContentPriceStatusAsync([NotNull] GetContentPriceStatusRequestDto requestDto);
    }
}
