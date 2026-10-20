namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface IAiProvider : IProvider<AiProviderType>
    {
        Task<ResultData<ChatCompletionResponseDto>> CompleteAsync([NotNull] ChatCompletionRequestDto requestDto);
    }
}
