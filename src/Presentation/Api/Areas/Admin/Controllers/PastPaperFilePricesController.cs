namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.PastPaper;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The past paper base price list: the base price per file extension that every paper file's price starts from
    /// (<c>PastPaperPricing</c>). An extension with no row is free. Admin only, never delegable: it sets what every paper
    /// costs. See docs/business/past-papers.md, "Prices".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class PastPaperFilePricesController(Lazy<ILogger<PastPaperFilePricesController>> logger, Lazy<IPastPaperFilePriceService> priceService)
        : ApiControllerBase<PastPaperFilePricesController>(logger)
    {
        /// <summary>Every row, by id.</summary>
        [HttpGet(""), Produces<ApiResponse<IEnumerable<PastPaperFilePriceViewModel>>>()]
        public async Task<IActionResult<IEnumerable<PastPaperFilePriceViewModel>>> GetPrices()
        {
            try
            {
                var result = await priceService.Value.GetPricesAsync();
                return Ok<IEnumerable<PastPaperFilePriceViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new PastPaperFilePriceViewModel
                    {
                        Id = t.Id,
                        Extensions = t.Extensions,
                        BasePrice = t.BasePrice,
                        SyncDate = t.SyncDate.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<IEnumerable<PastPaperFilePriceViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Adds a row. Answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> CreatePrice([NotNull, FromBody] ManagePastPaperFilePriceRequestViewModel request) =>
            ManageAsync(null, request);

        /// <summary>Edits a row's extensions and base price.</summary>
        [HttpPut("{id:long}"), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> UpdatePrice([FromRoute] long id, [NotNull, FromBody] ManagePastPaperFilePriceRequestViewModel request) =>
            ManageAsync(id, request);

        /// <summary>Deletes a row: its extensions become free.</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> DeletePrice([FromRoute] long id)
        {
            try
            {
                var result = await priceService.Value.DeletePriceAsync(id);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        private async Task<IActionResult<long>> ManageAsync(long? id, ManagePastPaperFilePriceRequestViewModel request)
        {
            try
            {
                var result = await priceService.Value.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto
                {
                    Id = id,
                    Extensions = request.Extensions,
                    BasePrice = request.BasePrice.GetValueOrDefault(),
                });
                return Ok<long>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<long>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
