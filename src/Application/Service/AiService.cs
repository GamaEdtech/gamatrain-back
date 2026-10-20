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
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class AiService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<AiService>> localizer
        , Lazy<ILogger<AiService>> logger, Lazy<IGenericFactory<IAiProvider, AiProviderType>> genericFactory, Lazy<IConfiguration> configuration)
        : LocalizableServiceBase<AiService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IAiService
    {
        public async Task<ResultData<ChatCompletionResponseDto>> CompleteAsync([NotNull] ChatCompletionRequestDto requestDto)
        {
            try
            {
                _ = configuration.Value.GetValue<string?>("Ai:Type").TryGetFromNameOrValue<AiProviderType, byte>(out var providerType);
                var provider = providerType is null ? null : genericFactory.Value.GetProvider(providerType);
                return provider is null
                    ? new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["AiProviderNotConfigured"] },] }
                    : await provider.CompleteAsync(requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
