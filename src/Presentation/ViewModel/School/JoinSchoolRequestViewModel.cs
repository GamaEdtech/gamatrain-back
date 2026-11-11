namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class JoinSchoolRequestViewModel
    {
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<SchoolMemberRole, byte>))]
        public SchoolMemberRole? Role { get; set; }

        /// <summary>Show my name on the school page (teachers and staff only). Also makes the profile Public.</summary>
        [Display]
        public bool ShowName { get; set; }
    }
}
