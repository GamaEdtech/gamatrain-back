namespace GamaEdtech.Infrastructure.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface ICaptchaProvider : IProvider<CaptchaProviderType>
    {
        Task<ResultData<bool>> VerifyCaptchaAsync(string? captcha);
    }
}
