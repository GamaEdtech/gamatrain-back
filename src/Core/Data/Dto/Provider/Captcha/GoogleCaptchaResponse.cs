namespace GamaEdtech.Data.Dto.Provider.Captcha
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.HttpProvider;

    public sealed class GoogleCaptchaResponse : IHttpResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
