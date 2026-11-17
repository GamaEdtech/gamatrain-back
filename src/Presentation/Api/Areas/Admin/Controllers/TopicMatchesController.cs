namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.TopicMatch;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.TopicMatch;

    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The review of the suggested matches between GAMA's topics and other boards' (gama-api's topic matching). Approving one
    /// links the two topics, so each board's paper and tutorial lists also show the other's. Changes are refused
    /// (<c>notNative</c>) until <c>Migration:WriteRoutes:Types.TopicMatches</c> and the types' writes are <c>Native</c>. See
    /// docs/business/tutorials.md, "Topic matching".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class TopicMatchesController(Lazy<ILogger<TopicMatchesController>> logger, Lazy<ITopicMatchAdminService> topicMatchAdminService)
        : ApiControllerBase<TopicMatchesController>(logger)
    {
        /// <summary>Suggested matches by GAMA topic, most confident first: <c>topicId</c> (either side), <c>gamaSubjectId</c>,
        /// <c>externalSubjectId</c>, <c>status</c> (name or value), <c>page</c>, <c>pageSize</c> (max 200).</summary>
        [HttpGet(""), Produces<ApiResponse<TopicMatchListResponseViewModel>>()]
        public Task<IActionResult<TopicMatchListResponseViewModel>> GetMatches([FromQuery] int? topicId, [FromQuery] int? gamaSubjectId,
            [FromQuery] int? externalSubjectId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
            AnswerAsync(async () =>
            {
                TopicMatchStatus? statusFilter = null;
                if (!string.IsNullOrEmpty(status) && !status.TryGetFromNameOrValue<TopicMatchStatus, byte>(out statusFilter))
                {
                    return new ResultData<TopicMatchListResponseViewModel>(OperationResult.NotValid) { Errors = [new() { Message = "Unknown status.", Reference = "status" }] };
                }

                var result = await topicMatchAdminService.Value.GetListAsync(new()
                {
                    TopicId = topicId,
                    GamaSubjectId = gamaSubjectId,
                    ExternalSubjectId = externalSubjectId,
                    Status = statusFilter,
                    Page = page,
                    PageSize = pageSize,
                });
                return new ResultData<TopicMatchListResponseViewModel>(result.OperationResult)
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new() { Total = result.Data.Total, Items = [.. result.Data.Items.Select(ToViewModel)] },
                };
            });

        /// <summary>Adds a match a person decided, waiting for approval; answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> AddMatch([NotNull, FromBody] TopicMatchAddRequestViewModel request) =>
            AnswerAsync(() => topicMatchAdminService.Value.AddAsync(new()
            {
                GamaTopicId = request.GamaTopicId.GetValueOrDefault(),
                ExternalTopicId = request.ExternalTopicId.GetValueOrDefault(),
                Confidence = request.Confidence,
                Reasoning = request.Reasoning,
            }));

        /// <summary>Approves a match: links its two topics.</summary>
        [HttpPost("{id:long}/approve"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> ApproveMatch([FromRoute] long id) =>
            AnswerAsync(() => topicMatchAdminService.Value.ApproveAsync(id, User.UserId()));

        /// <summary>Takes an approval back: removes the link; the match waits again.</summary>
        [HttpDelete("{id:long}/approve"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> UnapproveMatch([FromRoute] long id) =>
            AnswerAsync(() => topicMatchAdminService.Value.UnapproveAsync(id));

        /// <summary>Removes (rejects) a suggestion that is not approved.</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> RemoveMatch([FromRoute] long id) =>
            AnswerAsync(() => topicMatchAdminService.Value.RemoveAsync(id));

        /// <summary>Asks this backend's AI for matches of a topic among its corresponding subjects' topics; saves the confident
        /// ones (90+) as waiting and answers the topic's matches.</summary>
        [HttpPost("suggest/{topicId:int}"), Produces<ApiResponse<TopicMatchSuggestResponseViewModel>>()]
        public Task<IActionResult<TopicMatchSuggestResponseViewModel>> SuggestMatches([FromRoute] int topicId) =>
            AnswerAsync(async () =>
            {
                var result = await topicMatchAdminService.Value.SuggestAsync(topicId);
                return new ResultData<TopicMatchSuggestResponseViewModel>(result.OperationResult)
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new() { Added = result.Data.Added, Items = [.. result.Data.Items.Select(ToViewModel)], Errors = result.Data.Errors },
                };
            });

        private static TopicMatchResponseViewModel ToViewModel(TopicMatchDto match) => new()
        {
            Id = match.Id,
            Confidence = match.Confidence,
            Reasoning = match.Reasoning,
            AiModel = match.AiModel,
            Status = match.Status,
            ReviewedBy = match.ReviewedBy,
            CreationDate = match.CreationDate,
            Gama = ToViewModel(match.Gama),
            External = ToViewModel(match.External),
        };

        private static TopicPathViewModel ToViewModel(TopicPathDto path) => new()
        {
            Board = ToViewModel(path.Board),
            Grade = ToViewModel(path.Grade),
            Subject = ToViewModel(path.Subject),
            Topic = ToViewModel(path.Topic),
            Tutorial = ToViewModel(path.Tutorial),
        };

        private static TopicPathItemViewModel? ToViewModel(TopicPathItemDto? item) => item is null ? null : new() { Id = item.Id, Title = item.Title };

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<Task<ResultData<T>>> call)
        {
            try
            {
                var result = await call();
                return Ok<T>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<T>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
