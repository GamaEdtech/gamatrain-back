namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.UserActivity;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>Reads and podcast listens of the signed-in user. See docs/business/exams-and-content.md, "Reads and listens".</summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class ActivitiesController(Lazy<ILogger<ActivitiesController>> logger, Lazy<IUserActivityService> userActivityService)
        : ApiControllerBase<ActivitiesController>(logger)
    {
        /// <summary>Records a read or a listen, once per user, kind, post and day; a repeat is a success that changes nothing.</summary>
        [Permission(policy: null)]
        [HttpPost, Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> Record([NotNull, FromBody] RecordUserActivityRequestViewModel request)
        {
            try
            {
                var result = await userActivityService.Value.RecordAsync(User.UserId(), request.Type!, request.ContentId.GetValueOrDefault());
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
