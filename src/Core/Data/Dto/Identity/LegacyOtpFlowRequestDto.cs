namespace GamaEdtech.Data.Dto.Identity
{
    /// <summary>
    /// Shared shape for gama-api's /users/register and /users/recovery multi-step (request/resend_code/confirm/final) flows.
    /// </summary>
    public sealed class LegacyOtpFlowRequestDto
    {
        public required string Type { get; set; }
        public required string Identity { get; set; }
        public int? Code { get; set; }
        public string? Password { get; set; }
    }
}
