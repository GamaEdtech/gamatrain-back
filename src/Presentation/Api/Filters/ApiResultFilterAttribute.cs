namespace GamaEdtech.Presentation.Api.Filters
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain;
    using GamaEdtech.Presentation.Api.Models;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class ApiResultFilterAttribute : ActionFilterAttribute
    {
        public override void OnResultExecuting([NotNull] ResultExecutingContext context)
        {
            if (context.Result is ObjectResult objectResult1)
            {
                var apiResult = new ApiResult<object>(true, ApiResultStatusCode.Success, objectResult1.Value);
                context.Result = new JsonResult(apiResult.Data);
            }
            if (context.Result is ObjectResult objectResult && context.ModelState.IsValid)
            {
                var apiResult = new ApiResult<object>(true, ApiResultStatusCode.Success, objectResult.Value);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is OkResult)
            {
                var apiResult = new ApiResult(true, ApiResultStatusCode.Success);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is BadRequestResult)
            {
                var apiResult = new ApiResult(false, ApiResultStatusCode.BadRequest);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is BadRequestObjectResult badRequestObjectResult)
            {
                var message = badRequestObjectResult.Value?.ToString();

                if (badRequestObjectResult.Value is SerializableError error)
                {
                    var errorMessage = error.SelectMany(p => p.Value as string[] ?? []).Distinct();
                    message = string.Join("|", errorMessage);
                }

                var apiResult = new ApiResult<object>(
                    false,
                    ApiResultStatusCode.BadRequest,
                    message
                );

                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is ContentResult contentResult)
            {
                var apiResult = new ApiResult(true, ApiResultStatusCode.Success, contentResult.Content);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is NotFoundResult)
            {
                var apiResult = new ApiResult(false, ApiResultStatusCode.NotFound);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is NotFoundObjectResult notFoundObjectResult)
            {
                var apiResult = new ApiResult<object>(false, ApiResultStatusCode.NotFound, notFoundObjectResult.Value);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is ObjectResult objectResult2 && objectResult2.StatusCode == null
                && objectResult2.Value is not ApiResult)
            {
                var apiResult = new ApiResult<object>(true, ApiResultStatusCode.NotFound, objectResult2.Value);
                context.Result = new JsonResult(apiResult);
            }
            else if (context.Result is ChallengeResult challengeResult)
            {
                var apiResult = new ApiResult<object>(true, ApiResultStatusCode.Success, challengeResult);
                context.Result = new JsonResult(apiResult);
            }
            base.OnResultExecuting(context);
        }
    }

}
