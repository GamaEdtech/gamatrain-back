namespace GamaEdtech.Infrastructure.Provider.ContentDelivery
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Proxies gama-api's GET /tests/download/{id}/{type}[/{extraId}] (bearerAuth) to resolve a
    /// downloadable content item. Called with the downloading user's own legacy JWT - gama-api
    /// prices/gates per caller (see the price.paid field), so this can't be done with a
    /// service-level credential.
    /// </summary>
    public sealed class GamaApiContentDeliveryProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<GamaApiContentDeliveryProvider>> localizer
        , Lazy<ILogger<GamaApiContentDeliveryProvider>> logger)
        : InfrastructureBase<GamaApiContentDeliveryProvider>(httpProvider, localizer, logger), IContentDeliveryProvider
    {
        public ContentSource ProviderType => ContentSource.GamaApiLegacy;

        public async Task<ResultData<GetDownloadUrlResponseDto>> GetDownloadUrlAsync([NotNull] GetDownloadUrlRequestDto requestDto)
        {
            try
            {
                var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:TestDownload")!, requestDto.ExternalContentId, requestDto.FileType);
                if (requestDto.ExtraId is not null)
                {
                    uri = $"{uri}/{requestDto.ExtraId.Value.ToString(CultureInfo.InvariantCulture)}";
                }

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<GamaApiTestDownloadResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = [("Authorization", $"Bearer {requestDto.Token}")],
                });

                if (response is not { Status: 1, Data.Url: not null, Data.Price: not null })
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] };
                }

                var ownerId = response.Data.OwnerUID.ValueOf<long?>();
                return ownerId is null
                    ? new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] }
                    : new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Url = response.Data.Url,
                            Name = response.Data.Name,
                            OwnerExternalId = ownerId.Value,
                            Points = response.Data.Price.Price,
                            Paid = response.Data.Price.Paid,
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
