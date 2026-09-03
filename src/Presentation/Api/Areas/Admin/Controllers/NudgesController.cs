namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Nudge;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Admin CRUD for NudgeTemplate - the "robust for future use" part of the nudge system: adding/editing/
    /// disabling a nudge's copy is an admin-panel edit, not a deploy. See
    /// docs/business/notifications.md, "Nudge system".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Role.Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class NudgesController(Lazy<ILogger<NudgesController>> logger, Lazy<INudgeService> nudgeService)
        : ApiControllerBase<NudgesController>(logger)
    {
        [HttpGet("templates"), Produces<ApiResponse<ListDataSource<NudgeTemplateResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<NudgeTemplateResponseViewModel>>> GetNudgeTemplates([NotNull, FromQuery] NudgeTemplatesRequestViewModel request)
        {
            try
            {
                var result = await nudgeService.Value.GetNudgeTemplatesAsync(new() { PagingDto = request.PagingDto });
                return Ok<ListDataSource<NudgeTemplateResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new NudgeTemplateResponseViewModel
                        {
                            Id = t.Id,
                            NudgeType = t.NudgeType,
                            Subject = t.Subject,
                            Body = t.Body,
                            CtaLabel = t.CtaLabel,
                            CtaUrl = t.CtaUrl,
                            IsActive = t.IsActive,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<NudgeTemplateResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("templates/{id:int}"), Produces<ApiResponse<NudgeTemplateResponseViewModel>>()]
        public async Task<IActionResult<NudgeTemplateResponseViewModel>> GetNudgeTemplate([FromRoute] int id)
        {
            try
            {
                var result = await nudgeService.Value.GetNudgeTemplateAsync(new IdEqualsSpecification<NudgeTemplate, int>(id));
                return Ok<NudgeTemplateResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        NudgeType = result.Data.NudgeType,
                        Subject = result.Data.Subject,
                        Body = result.Data.Body,
                        CtaLabel = result.Data.CtaLabel,
                        CtaUrl = result.Data.CtaUrl,
                        IsActive = result.Data.IsActive,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<NudgeTemplateResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("templates"), Produces<ApiResponse<ManageNudgeTemplateResponseViewModel>>()]
        public async Task<IActionResult<ManageNudgeTemplateResponseViewModel>> CreateNudgeTemplate([NotNull] ManageNudgeTemplateRequestViewModel request)
        {
            try
            {
                var result = await nudgeService.Value.ManageNudgeTemplateAsync(new()
                {
                    NudgeType = request.NudgeType,
                    Subject = request.Subject,
                    Body = request.Body,
                    CtaLabel = request.CtaLabel,
                    CtaUrl = request.CtaUrl,
                    IsActive = request.IsActive,
                });
                return Ok<ManageNudgeTemplateResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageNudgeTemplateResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("templates/{id:int}"), Produces<ApiResponse<ManageNudgeTemplateResponseViewModel>>()]
        public async Task<IActionResult<ManageNudgeTemplateResponseViewModel>> UpdateNudgeTemplate([FromRoute] int id, [NotNull, FromBody] ManageNudgeTemplateRequestViewModel request)
        {
            try
            {
                var result = await nudgeService.Value.ManageNudgeTemplateAsync(new()
                {
                    Id = id,
                    NudgeType = request.NudgeType,
                    Subject = request.Subject,
                    Body = request.Body,
                    CtaLabel = request.CtaLabel,
                    CtaUrl = request.CtaUrl,
                    IsActive = request.IsActive,
                });
                return Ok<ManageNudgeTemplateResponseViewModel>(new(result.Errors) { Data = new() { Id = result.Data }, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageNudgeTemplateResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("templates/{id:int}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemoveNudgeTemplate([FromRoute] int id)
        {
            try
            {
                var result = await nudgeService.Value.RemoveNudgeTemplateAsync(new IdEqualsSpecification<NudgeTemplate, int>(id));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
