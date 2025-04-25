namespace GamaEdtech.Presentation.Api.Models
{
    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain;

    using Microsoft.AspNetCore.Mvc;

    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Serialization;

    public class ApiResult<TData>(bool isSuccess, ApiResultStatusCode statusCode, TData? data, string? message = null)
        : ApiResult(isSuccess, statusCode, message)
        where TData : class
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TData? Data { get; set; } = data;

        #region Implicit Operators
        public static implicit operator ApiResult<TData>(TData data)
            => new(true, ApiResultStatusCode.Success, data);

        public static implicit operator ApiResult<TData>(FileStreamResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            return new ApiResult<TData>(true, ApiResultStatusCode.Success, null);
        }

        public static implicit operator ApiResult<TData>(OkResult _)
            => new(true, ApiResultStatusCode.Success, null);

        public static implicit operator ApiResult<TData>(OkObjectResult result) =>
            new(true, ApiResultStatusCode.Success, result?.Value as TData ?? default);

        public static implicit operator ApiResult<TData>(BadRequestResult _)
            => new(false, ApiResultStatusCode.BadRequest, null);

        public static implicit operator ApiResult<TData>([NotNull] BadRequestObjectResult result)
        {
            var message = result.Value?.ToString();
            if (result.Value is SerializableError errors)
            {
                var errorMessages = errors.SelectMany(p => p.Value as string[]
                ?? []).Distinct();
                message = string.Join(" | ", errorMessages);
            }

            return new ApiResult<TData>(false, ApiResultStatusCode.BadRequest, null, message);
        }

        public static implicit operator ApiResult<TData>(ContentResult result)
            => new(true, ApiResultStatusCode.Success, null, result?.Content);

        public static implicit operator ApiResult<TData>(NotFoundResult _)
            => new(false, ApiResultStatusCode.NotFound, null);

        public static implicit operator ApiResult<TData>(NotFoundObjectResult result)
            => new(false, ApiResultStatusCode.NotFound, result?.Value as TData ?? default);

        public new virtual ApiResult ToApiResult() => this;
        #endregion
    }
    public class ApiResult(bool isSuccess, ApiResultStatusCode statusCode, string? message = null)
    {
        public bool IsSuccess { get; set; } = isSuccess;
        public ApiResultStatusCode StatusCode { get; set; } = statusCode;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Message { get; set; } = message ?? statusCode.ToDisplay(DisplayProperty.Name);

        #region Implicit Operators
        public static implicit operator ApiResult(OkResult _) =>
            new(true, ApiResultStatusCode.Success);
        public static implicit operator ApiResult(FileStreamResult _)
            => new(true, ApiResultStatusCode.Success, null);
        public static implicit operator ApiResult(BadRequestResult _)
            => new(false, ApiResultStatusCode.BadRequest);

        public static implicit operator ApiResult(BadRequestObjectResult result)
        {
            var message = result?.Value?.ToString();
            if (result != null && result.Value is SerializableError errors)
            {
                var errorMessages = errors.SelectMany(p => (string[])p.Value).Distinct();
                message = string.Join(" | ", errorMessages);
            }
            return new ApiResult(false, ApiResultStatusCode.BadRequest, message);
        }

        public static implicit operator ApiResult(ContentResult result)
            => new(true, ApiResultStatusCode.Success, result?.Content);

        public static implicit operator ApiResult(NotFoundResult _)
            => new(false, ApiResultStatusCode.NotFound);

        public ApiResult ToApiResult() => this;
        #endregion
    }
}
