namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolMembersResponseViewModel
    {
        /// <summary>Teachers and staff who agreed to be named and have a Public profile (at most 50).</summary>
        public IEnumerable<NamedSchoolMemberViewModel> NamedMembers { get; set; } = [];

        /// <summary>How many members the school has per role, named or not.</summary>
        public IEnumerable<SchoolMemberCountViewModel> Counts { get; set; } = [];
    }

    public sealed class NamedSchoolMemberViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<SchoolMemberRole, byte>))]
        public SchoolMemberRole? Role { get; set; }

        public string? FullName { get; set; }

        public string? AvatarUri { get; set; }

        /// <summary>For the link to the public profile (identities/profiles/{handle}).</summary>
        public string? Handle { get; set; }
    }

    public sealed class SchoolMemberCountViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<SchoolMemberRole, byte>))]
        public SchoolMemberRole? Role { get; set; }

        public int Count { get; set; }
    }
}
