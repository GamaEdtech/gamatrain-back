namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Presentation.ViewModel.Content;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Resolves downloadable content from external sources (gama-api's legacy tests today).
    /// Combines the source lookup, the downloader's charge (quota-then-points), and the content
    /// owner's commission accrual into a single call - see docs/business/content-delivery.md.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class DownloadsController(Lazy<ILogger<DownloadsController>> logger, Lazy<IContentDeliveryService> contentDeliveryService)
        : ApiControllerBase<DownloadsController>(logger)
    {
        [HttpPost("tests"), Produces(typeof(ApiResponse<DownloadContentResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<DownloadContentResponseViewModel>> DownloadTest([NotNull][FromBody] DownloadTestRequestViewModel request)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<DownloadContentResponseViewModel>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await contentDeliveryService.Value.DownloadTestAsync(new DownloadTestRequestDto
                {
                    UserId = User.UserId(),
                    Token = token,
                    Id = request.Id.GetValueOrDefault(),
                    FileType = request.FileType!,
                    ExtraId = request.ExtraId,
                    ContentType = request.ContentType!,
                });

                return Ok<DownloadContentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Url = result.Data.Url,
                        Name = result.Data.Name,
                        Spent = result.Data.Spent,
                        PaidBy = result.Data.PaidBy,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<DownloadContentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
