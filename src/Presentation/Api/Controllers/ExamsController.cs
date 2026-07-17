namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.Exam;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [Permission(policy: null)]
    public class ExamsController(Lazy<ILogger<ExamsController>> logger, Lazy<IExamService> examService)
        : ApiControllerBase<ExamsController>(logger)
    {
        [HttpGet("export"), Produces(typeof(IActionResult))]
        public async Task<IActionResult> Export([FromHeader(Name = "SecretKey")] string secretKey, [NotNull][FromQuery] ExportExamRequestViewModel request)
        {
            try
            {
                if (string.IsNullOrEmpty(secretKey))
                {
                    return Ok<Void>(new(new Error { Message = "Missing SecretKey" }));
                }

                var result = await examService.Value.ExportExamAsync(new()
                {
                    UserId = User.UserId(),
                    SecretKey = secretKey,
                    ExamId = request.Id.GetValueOrDefault(),
                    FileType = request.FileType!,
                    Watermark = request.Watermark,
                    Duration = request.Duration,
                    Url = $"{Request.Scheme}://{Request.Host.ToString().TrimEnd('/')}",
                });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<Void>(new(result.Errors));
                }

                System.Net.Mime.ContentDisposition disposition = new()
                {
                    FileName = $"{result.Data!.FileName}{request.FileType!.Extension}",
                    Inline = false,
                };
                Response.Headers.Append("Content-Disposition", disposition.ToString());
                Response.Headers.Append("X-Content-Type-Options", "nosniff");

                return new FileContentResult(result!.Data!.Content!, "application/octet-stream");
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
