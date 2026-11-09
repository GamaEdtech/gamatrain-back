namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageUserPermissionsRequestViewModel
    {
        [Display]
        public IEnumerable<string>? Permissions { get; set; }

        [Display]
        [JsonConverter(typeof(FlagsEnumerationConverter<SystemClaim>))]
        public SystemClaim? SystemClaims { get; set; }

        [Display]
        [JsonConverter(typeof(FlagsEnumerationConverter<Role>))]
        public Role? Roles { get; set; }

        /// <summary>The calling admin's current 6-digit authenticator code; required (step-up).</summary>
        [Display]
        [StringLength(10)]
        public string? TwoFactorCode { get; set; }
    }
}
