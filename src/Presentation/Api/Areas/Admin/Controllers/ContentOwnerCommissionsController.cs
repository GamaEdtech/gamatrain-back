namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Content;
    using GamaEdtech.Presentation.ViewModel.Content;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>Admin-wide report of accrued content-owner commissions across all owners - no paid/payout state exists yet, see ContentOwnerCommission.</summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class ContentOwnerCommissionsController(Lazy<ILogger<ContentOwnerCommissionsController>> logger, Lazy<IContentDeliveryService> contentDeliveryService)
        : ApiControllerBase<ContentOwnerCommissionsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<ContentOwnerCommissionListResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<ContentOwnerCommissionListResponseViewModel>>> GetCommissions([NotNull, FromQuery] AdminContentOwnerCommissionsListRequestViewModel request)
        {
            try
            {
                ISpecification<ContentOwnerCommission>? specification = null;

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = new CreationDateBetweenSpecification<ContentOwnerCommission>(request.StartDate, request.EndDate);
                }

                if (request.OwnerUserId.HasValue)
                {
                    var spec = new OwnerUserIdEqualsSpecification(request.OwnerUserId.Value);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await contentDeliveryService.Value.GetContentOwnerCommissionsAsync(new ListRequestDto<ContentOwnerCommission>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });

                return Ok<ListDataSource<ContentOwnerCommissionListResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new ContentOwnerCommissionListResponseViewModel
                        {
                            Id = t.Id,
                            OwnerUserId = t.OwnerUserId,
                            OwnerFirstName = t.OwnerFirstName,
                            OwnerLastName = t.OwnerLastName,
                            DownloaderUserId = t.DownloaderUserId,
                            Reason = t.Reason,
                            Source = t.Source,
                            ContentType = t.ContentType,
                            ExternalContentId = t.ExternalContentId,
                            ExternalFileType = t.ExternalFileType,
                            ExternalExtraId = t.ExternalExtraId,
                            Points = t.Points,
                            CommissionPercent = t.CommissionPercent,
                            AmountUsd = t.AmountUsd,
                            CreationDate = t.CreationDate,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ListDataSource<ContentOwnerCommissionListResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
