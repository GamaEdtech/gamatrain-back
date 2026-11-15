namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.UserActivity;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Admin view of reads and podcast listens. Admins only and deliberately not delegable: it shows people's reading history.
    /// See docs/business/exams-and-content.md, "Reads and listens".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Role.Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class ActivitiesController(Lazy<ILogger<ActivitiesController>> logger, Lazy<IUserActivityService> userActivityService)
        : ApiControllerBase<ActivitiesController>(logger)
    {
        private const int DefaultDays = 30;
        private const int DefaultSize = 20;

        /// <summary>The posts with the most readers in the window, with listeners and full listens (different users each).</summary>
        [HttpGet("posts"), Produces(typeof(ApiResponse<IEnumerable<PostActivityResponseViewModel>>))]
        public async Task<IActionResult<IEnumerable<PostActivityResponseViewModel>>> GetTopPosts([NotNull, FromQuery] TopPostsRequestViewModel request)
        {
            try
            {
                var result = await userActivityService.Value.GetTopPostsAsync(request.Days ?? DefaultDays, request.Size ?? DefaultSize);
                return Ok<IEnumerable<PostActivityResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new PostActivityResponseViewModel
                    {
                        PostId = t.PostId,
                        Title = t.Title,
                        Readers = t.Readers,
                        Listeners = t.Listeners,
                        FullListens = t.FullListens,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<IEnumerable<PostActivityResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>One user's reads and listens in the window, newest first (at most 200).</summary>
        [HttpGet("users/{userId:long}"), Produces(typeof(ApiResponse<IEnumerable<UserActivityItemResponseViewModel>>))]
        public async Task<IActionResult<IEnumerable<UserActivityItemResponseViewModel>>> GetUserActivity([FromRoute] long userId, [FromQuery] int? days)
        {
            try
            {
                var result = await userActivityService.Value.GetUserActivityAsync(userId, days ?? DefaultDays);
                return Ok<IEnumerable<UserActivityItemResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new UserActivityItemResponseViewModel { Day = t.Day, Type = t.Type, PostId = t.PostId, Title = t.Title }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<IEnumerable<UserActivityItemResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
