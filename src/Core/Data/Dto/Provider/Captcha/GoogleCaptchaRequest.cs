namespace GamaEdtech.Data.Dto.Provider.Captcha
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.HttpProvider;

    public sealed class GoogleCaptchaRequest : IHttpRequest
    {
        [JsonPropertyName("secret")]
        public string? Secret { get; set; }

        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
}
