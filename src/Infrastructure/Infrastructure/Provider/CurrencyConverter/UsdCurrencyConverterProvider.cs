namespace GamaEdtech.Infrastructure.Provider.CurrencyConverter
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.CurrencyConverter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class UsdCurrencyConverterProvider(Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<UsdCurrencyConverterProvider>> localizer, Lazy<ILogger<UsdCurrencyConverterProvider>> logger)
        : InfrastructureBase<UsdCurrencyConverterProvider>(httpProvider, localizer, logger), ICurrencyConverterProvider
    {
        public Currency ProviderType => Currency.USD;

        public async Task<ResultData<ConvertResponseDto>> GetPointsAsync([NotNull] ConvertRequestDto requestDto)
        {
            try
            {
                const long centMetrics = 100;

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Point = await Task.FromResult((long)requestDto.Amount * centMetrics),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}
