namespace GamaEdtech.Infrastructure.Provider.ContentDelivery
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;

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
    /// Proxies gama-api's three download-URL endpoints, selected by DownloadContentType:
    /// GET /tests/download/{id}/{type}[/{extraId}] for PastPaper (returns ownerUID + price.paid),
    /// GET /files/download/{id} for Multimedia, GET /exams/download/{id} for Exam (both of the
    /// latter return only {url, name} - no owner, no price, so ContentDeliveryService never accrues
    /// commission for those two, even though they can now be charged - see GetContentPriceStatusAsync
    /// below). DownloadContentType is a dedicated 3-member enum, deliberately separate from the
    /// broader ContentType (which also has a Test member, relevant only to the unrelated
    /// games/spends endpoint) - see docs/business/content-delivery.md. Called with the downloading
    /// user's own legacy JWT, never a service-level credential, since gama-api prices/gates per
    /// caller.
    ///
    /// Also proxies gama-api's three *detail* endpoints (GetContentPriceStatusAsync) -
    /// GET /tests/{id}, GET /files/{id}, GET /exams/{id} - a side-effect-free price/paid lookup,
    /// confirmed live (2026-07-20) to never change `paid` no matter how many times it's called,
    /// unlike the download endpoints above. ContentDeliveryService calls this first so it only ever
    /// calls a (side-effecting) download endpoint once payment is already settled - closing a real
    /// bug where a failed local charge still left the download endpoint's own call marking the item
    /// paid on gama-api's side, letting a retried request through for free.
    /// </summary>
    public sealed class GamaApiContentDeliveryProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<GamaApiContentDeliveryProvider>> localizer
        , Lazy<ILogger<GamaApiContentDeliveryProvider>> logger)
        : InfrastructureBase<GamaApiContentDeliveryProvider>(httpProvider, localizer, logger), IContentDeliveryProvider
    {
        public ContentSource ProviderType => ContentSource.GamaApiLegacy;

        public async Task<ResultData<GetDownloadUrlResponseDto>> GetDownloadUrlAsync([NotNull] GetDownloadUrlRequestDto requestDto)
        {
            // Captured by postCallHandler below before the body is read, so it's available even if reading/
            // decoding the body then throws (e.g. gama-api's 401/403 body isn't valid JSON) - same pattern as
            // CoreProvider.GetDashboardAsync's legacyStatusCode, checked in both the normal-return path and the
            // catch block below.
            HttpStatusCode? legacyStatusCode = null;

            try
            {
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
                }, postCallHandler: r =>
                {
                    legacyStatusCode = r.StatusCode;
                    return Task.CompletedTask;
                });

                if (IsAuthRejection(legacyStatusCode))
                {
                    // gama-api rejected the caller's own token for this call specifically - the caller
                    // (ContentDeliveryService) may already have charged the user for this download before making
                    // this call, and needs to know this was an auth rejection (as opposed to content not found,
                    // or gama-api being down) to refund correctly and to let DownloadsController propagate a real
                    // HTTP 401, same as IdentitiesController.GetDashboard already does for the same failure shape.
                    return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true } };
                }

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
                if (IsAuthRejection(legacyStatusCode))
                {
                    // gama-api's 401/403 body wasn't valid JSON, so reading/decoding it above threw - the status
                    // code was still captured before that happened. Still not an infrastructure failure: gama-api
                    // answered, it just rejected the token.
                    return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true } };
                }

                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }

            static bool IsAuthRejection(HttpStatusCode? statusCode) => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        }

        public Task<ResultData<GetContentPriceStatusResponseDto>> GetContentPriceStatusAsync([NotNull] GetContentPriceStatusRequestDto requestDto) => requestDto.ContentType switch
        {
            _ when requestDto.ContentType == DownloadContentType.Multimedia => GetFilePriceStatusAsync(requestDto),
            _ when requestDto.ContentType == DownloadContentType.Exam => GetExamPriceStatusAsync(requestDto),
            _ => GetTestPriceStatusAsync(requestDto),
        };

        private async Task<ResultData<GetContentPriceStatusResponseDto>> GetTestPriceStatusAsync(GetContentPriceStatusRequestDto requestDto)
        {
            try
            {
                var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:TestDetails")!, requestDto.ExternalContentId);

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<GamaApiPaperDetailsResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = [("Authorization", $"Bearer {requestDto.Token}")],
                });

                if (response is not { Status: 1, Data.Files: not null })
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] };
                }

                var fileStatus = requestDto.FileType switch
                {
                    "pdf" => response.Data.Files.Pdf,
                    "word" => response.Data.Files.Word,
                    "answer" => response.Data.Files.Answer,
                    _ => null,
                };

                return new(OperationResult.Succeeded)
                {
                    Data = new() { Points = fileStatus?.Price, Paid = fileStatus?.Paid ?? false },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private async Task<ResultData<GetContentPriceStatusResponseDto>> GetFilePriceStatusAsync(GetContentPriceStatusRequestDto requestDto)
        {
            try
            {
                var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:MultimediaDetails")!, requestDto.ExternalContentId);

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<GamaApiMultimediaDetailsResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = [("Authorization", $"Bearer {requestDto.Token}")],
                });

                return response is not { Status: 1, Data.Files: not null }
                    ? new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] }
                    : new(OperationResult.Succeeded) { Data = new() { Points = response.Data.Files.Price, Paid = response.Data.Files.Paid } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private async Task<ResultData<GetContentPriceStatusResponseDto>> GetExamPriceStatusAsync(GetContentPriceStatusRequestDto requestDto)
        {
            try
            {
                var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:ExamApiDetails")!, requestDto.ExternalContentId);

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<GamaApiExamDetailsResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = [("Authorization", $"Bearer {requestDto.Token}")],
                });

                if (response is not { Status: 1, Data.Price: not null })
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] };
                }

                // Only `price.pdf` (downloading the exam) - `price.participation` (taking the exam) is
                // a different action entirely, unrelated to this download endpoint.
                return new(OperationResult.Succeeded)
                {
                    Data = new() { Points = response.Data.Price.Pdf?.Price, Paid = response.Data.Price.Pdf?.Paid ?? false },
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
            if (requestDto.ContentType == DownloadContentType.Multimedia)
            {
                return string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:FileDownload")!, requestDto.ExternalContentId);
            }

            if (requestDto.ContentType == DownloadContentType.Exam)
            {
                return string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:ExamDownload")!, requestDto.ExternalContentId);
            }

            // PastPaper
            if (string.IsNullOrEmpty(requestDto.FileType))
            {
                return null;
            }

            var uri = string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>("Core:TestDownload")!, requestDto.ExternalContentId, requestDto.FileType);
            return requestDto.ExtraId is null ? uri : $"{uri}/{requestDto.ExtraId.Value.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
