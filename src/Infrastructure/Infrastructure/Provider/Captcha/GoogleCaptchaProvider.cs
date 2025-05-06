namespace GamaEdtech.Infrastructure.Provider.Captcha
{
    using System;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.Captcha;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class GoogleCaptchaProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider
        , Lazy<IStringLocalizer<GoogleCaptchaProvider>> localizer, Lazy<ILogger<GoogleCaptchaProvider>> logger)
        : InfrastructureBase<GoogleCaptchaProvider>(httpProvider, localizer, logger), ICaptchaProvider
    {
        public CaptchaProviderType ProviderType => CaptchaProviderType.Google;

        public async Task<ResultData<bool>> VerifyCaptchaAsync(string? captcha)
        {
            try
            {
                var apiUri = configuration.Value.GetValue<string>("Captcha:Google:Uri");
                var secret = configuration.Value.GetValue<string>("Captcha:Google:SecretKey");

                var response = await HttpProvider.Value.GetAsync<GoogleCaptchaRequest, GoogleCaptchaResponse, GoogleCaptchaRequest>(new()
                {
                    Uri = apiUri,
                    Request = new(),
                    Body = new()
                    {
                        Secret = secret,
                        Response = captcha,
                    },
                });

                return new(OperationResult.Succeeded) { Data = response?.Success ?? false };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}
