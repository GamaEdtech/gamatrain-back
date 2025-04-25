namespace GamaEdtech.Domain
{
    using System.ComponentModel.DataAnnotations;

    public enum ApiResultStatusCode
    {
        None = 0,
        [Display(Name = "عملیات با موفقیت انجام شد")]
        Success = 200,
        [Display(Name = "خطایی در سرور رخ داده است")]
        ServerError = 500,
        [Display(Name = "پارامتر های ارسالی معتبر نیست")]
        BadRequest = 400,
        [Display(Name = "یافت نشد")]
        NotFound = 404,
        [Display(Name = "لیست خالی است")]
        NoContent = 204,
        [Display(Name = "خطایی در پردازش رخ داده است")]
        LogicError = 409,
        [Display(Name = "unAuthentication")]
        UnAuthorized = 401,
        [Display(Name = "خطای احراز هویت خودکار" + "\n" + "unRefreshAuthentication")]
        UnRefreshAuthorized = 402,
        [Display(Name = "خطای ممنوع یا سطح دسترسی" + "\n" + "unAuthorizeation")]
        Forbidden = 403,
        [Display(Name = "خطای تکرای بودن پارامتر" + "\n" + "is repetitive")]
        Confilict = 303,
        [Display(Name = "موجود غیرقابل پردازش" + "\n" + "unprocessable entity")]
        UnprocessableEntity = 422,
        [Display(Name = "خطای محدودیت درخواست" + "\n" + "Too Many Requests")]
        TooManyRequests = 429
    }
}
