namespace GamaEdtech.Domain.DataAccess.Responses
{
    using GamaEdtech.Common.Core.Extensions;

    public class BaseResponse
    {
        public CustomDateTimeFormat CreateDate { get; init; }
        public CustomDateTimeFormat LastUpdatedDate { get; init; }
    }
}
