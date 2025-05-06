namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class CaptchaProviderType : Enumeration<CaptchaProviderType, byte>
    {
        [Display]
        public static readonly CaptchaProviderType Google = new(nameof(Google), 0);

        public CaptchaProviderType()
        {
        }

        public CaptchaProviderType(string name, byte value) : base(name, value)
        {
        }
    }
}
