namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Provider.Ai;

    /// <summary>
    /// The one entry point for text generation. Features build their own prompts and call this; it picks the
    /// provider configured in <c>Ai:Type</c>, so no feature talks to an AI vendor or holds its key directly.
    /// </summary>
    [Injectable]
    public interface IAiService
    {
        Task<ResultData<ChatCompletionResponseDto>> CompleteAsync([NotNull] ChatCompletionRequestDto requestDto);
    }
}
