namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UserSchoolResponseViewModel
    {
        public long SchoolId { get; set; }

        public string? SchoolName { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SchoolMemberRole, byte>))]
        public SchoolMemberRole? Role { get; set; }

        public bool ShowName { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
