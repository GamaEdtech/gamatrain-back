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
    /// Proxies gama-api's three download-URL endpoints, selected by ContentType:
    /// GET /tests/download/{id}/{type}[/{extraId}] for PastPaper (priced/gated per caller -
    /// returns ownerUID + price.paid), GET /files/download/{id} for Multimedia, GET
    /// /exams/download/{id} for Exam (both of the latter return only {url, name} - no owner, no
    /// price, so ContentDeliveryService never charges or accrues commission for those two). Any
    /// other ContentType (notably the historical ContentType.Test - kept defined only because an
    /// old migration compiles against it, see docs/business/content-delivery.md) is rejected here
    /// as unsupported; this content-delivery feature only ever exposes PastPaper/Multimedia/Exam.
    /// Called with the downloading user's own legacy JWT, never a service-level credential, since
    /// gama-api prices/gates per caller.
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
                if (requestDto.ContentType != ContentType.PastPaper && requestDto.ContentType != ContentType.Multimedia && requestDto.ContentType != ContentType.Exam)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["UnsupportedContentType"], }] };
                }

                var uri = BuildUri(requestDto);
                if (uri is null)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["FileTypeRequired"], }] };
                }

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<GamaApiDownloadResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = [("Authorization", $"Bearer {requestDto.Token}")],
                });

                if (response is not { Status: 1, Data.Url: not null })
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] };
                }

                var ownerId = response.Data.OwnerUID.ValueOf<long?>();
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Url = response.Data.Url,
                        Name = response.Data.Name,
                        OwnerExternalId = ownerId,
                        Points = response.Data.Price?.Price,
                        Paid = response.Data.Price?.Paid,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private string? BuildUri(GetDownloadUrlRequestDto requestDto)
        {
            if (requestDto.ContentType == ContentType.Multimedia)
            {
                return string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:FileDownload")!, requestDto.ExternalContentId);
            }

            if (requestDto.ContentType == ContentType.Exam)
            {
                return string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:ExamDownload")!, requestDto.ExternalContentId);
            }

            // PastPaper (the only ContentType left after the caller's validity check above)
            if (string.IsNullOrEmpty(requestDto.FileType))
            {
                return null;
            }

            var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:TestDownload")!, requestDto.ExternalContentId, requestDto.FileType);
            return requestDto.ExtraId is null ? uri : $"{uri}/{requestDto.ExtraId.Value.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
