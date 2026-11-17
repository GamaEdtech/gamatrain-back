namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Tutorial;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Tutorial;

    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The admin panel's tutorials, on this backend's copy: the list, one tutorial, adding, editing, the status, deleting and
    /// AI keywords. Changes are refused (<c>notNative</c>) while <c>Migration:WriteRoutes:Tutorials</c> is not <c>Native</c>.
    /// Images in the content are uploaded through <c>EditorImages</c>. See docs/business/tutorials.md, "Admin".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class TutorialsController(Lazy<ILogger<TutorialsController>> logger, Lazy<ITutorialAdminService> tutorialAdminService)
        : ApiControllerBase<TutorialsController>(logger)
    {
        /// <summary>Tutorials in any status, latest change first: <c>status</c> (name or value), <c>boardId</c>, <c>gradeId</c>,
        /// <c>subjectId</c>, <c>topicId</c>, <c>title</c> (the title, description or keywords contain it, or the id),
        /// <c>ownerUserId</c>, <c>page</c>, <c>pageSize</c> (max 100).</summary>
        [HttpGet(""), Produces<ApiResponse<TutorialListResponseViewModel>>()]
        public Task<IActionResult<TutorialListResponseViewModel>> GetTutorials([FromQuery] string? status, [FromQuery] int? boardId, [FromQuery] int? gradeId,
            [FromQuery] int? subjectId, [FromQuery] int? topicId, [FromQuery] string? title, [FromQuery] long? ownerUserId,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            AnswerAsync(async () =>
            {
                TutorialStatus? statusFilter = null;
                if (!string.IsNullOrEmpty(status) && !status.TryGetFromNameOrValue<TutorialStatus, byte>(out statusFilter))
                {
                    return new ResultData<TutorialListResponseViewModel>(OperationResult.NotValid) { Errors = [new() { Message = "Unknown status.", Reference = "status" }] };
                }

                var result = await tutorialAdminService.Value.GetListAsync(new()
                {
                    Status = statusFilter,
                    BoardId = boardId,
                    GradeId = gradeId,
                    SubjectId = subjectId,
                    TopicId = topicId,
                    Title = title,
                    OwnerUserId = ownerUserId,
                    Page = page,
                    PageSize = pageSize,
                });
                return new ResultData<TutorialListResponseViewModel>(result.OperationResult)
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new() { Total = result.Data.Total, Items = [.. result.Data.Items.Select(t => Fill(new TutorialItemResponseViewModel(), t))] },
                };
            });

        /// <summary>One tutorial in any status, for the edit form (its HTML with image links on this backend's storage).</summary>
        [HttpGet("{id:long}"), Produces<ApiResponse<TutorialResponseViewModel>>()]
        public Task<IActionResult<TutorialResponseViewModel>> GetTutorial([FromRoute] long id) =>
            AnswerAsync(async () =>
            {
                var result = await tutorialAdminService.Value.GetAsync(id);
                if (result.Data is null)
                {
                    return new ResultData<TutorialResponseViewModel>(result.OperationResult) { Errors = result.Errors };
                }

                var data = Fill(new TutorialResponseViewModel(), result.Data);
                data.Content = result.Data.Content;
                data.Description = result.Data.Description;
                return new ResultData<TutorialResponseViewModel>(OperationResult.Succeeded) { Data = data };
            });

        /// <summary>Adds a tutorial owned by the caller; answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> CreateTutorial([NotNull, FromBody] TutorialRequestViewModel request) =>
            AnswerAsync(() => tutorialAdminService.Value.CreateAsync(User.UserId(), ToDto(request)));

        [HttpPut("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> UpdateTutorial([FromRoute] long id, [NotNull, FromBody] TutorialRequestViewModel request) =>
            AnswerAsync(() => tutorialAdminService.Value.UpdateAsync(id, ToDto(request)));

        /// <summary>Publishes (<c>Confirmed</c>), holds (<c>Pending</c>), rejects or deactivates a tutorial; only
        /// <c>Confirmed</c> ones are on the website.</summary>
        [HttpPut("{id:long}/status"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> ChangeStatus([FromRoute] long id, [NotNull, FromBody] TutorialStatusRequestViewModel request) =>
            AnswerAsync(() => tutorialAdminService.Value.ChangeStatusAsync(id, request.Status!));

        /// <summary>Deletes a tutorial for good (its image rows too; the files stay in storage).</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> DeleteTutorial([FromRoute] long id) =>
            AnswerAsync(() => tutorialAdminService.Value.DeleteAsync(id));

        /// <summary>Asks the AI for the tutorial's keywords (saved on it) and learning clusters (shown only).</summary>
        [HttpPost("{id:long}/keywords"), Produces<ApiResponse<TutorialKeywordsResponseViewModel>>()]
        public Task<IActionResult<TutorialKeywordsResponseViewModel>> GenerateKeywords([FromRoute] long id) =>
            AnswerAsync(async () =>
            {
                var result = await tutorialAdminService.Value.GenerateKeywordsAsync(id);
                return new ResultData<TutorialKeywordsResponseViewModel>(result.OperationResult)
                {
                    Errors = result.Errors,
                    Data = result.Data is null ? null : new() { Keywords = result.Data.Keywords, Clusters = result.Data.Clusters },
                };
            });

        private static TutorialEditDto ToDto(TutorialRequestViewModel request) => new()
        {
            Title = request.Title,
            Content = request.Content,
            Description = request.Description,
            BoardId = request.BoardId,
            GradeId = request.GradeId,
            CourseId = request.CourseId,
            SubjectId = request.SubjectId,
            TopicIds = request.TopicIds,
            Keywords = request.Keywords,
            Status = request.Status,
        };

        private static T Fill<T>(T data, TutorialAdminItemDto item)
            where T : TutorialItemResponseViewModel
        {
            data.Id = item.Id;
            data.Title = item.Title;
            data.Status = item.Status;
            data.BoardId = item.BoardId;
            data.BoardTitle = item.BoardTitle;
            data.GradeId = item.GradeId;
            data.GradeTitle = item.GradeTitle;
            data.CourseId = item.CourseId;
            data.SubjectId = item.SubjectId;
            data.SubjectTitle = item.SubjectTitle;
            data.Topics = [.. item.Topics.Select(t => new TutorialTopicViewModel { Id = t.Id, Title = t.Title })];
            data.Keywords = item.Keywords;
            data.OwnerUserId = item.OwnerUserId;
            data.OwnerName = item.OwnerName;
            data.ViewCount = item.ViewCount;
            data.CreationDate = item.CreationDate;
            data.LastModifyDate = item.LastModifyDate;
            return data;
        }

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
