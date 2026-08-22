namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    public sealed class ProfileSettingsRequestViewModel
    {
        [Display]
        public int? CityId { get; set; }

        [Display]
        public long? SchoolId { get; set; }

        [Display]
        public string? FirstName { get; set; }

        [Display]
        public string? LastName { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<GenderType, byte>))]
        public GenderType? Gender { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<ProfileVisibility, byte>))]
        public ProfileVisibility? ProfileVisibility { get; set; }

        [Display]
        public int? Board { get; set; }

        [Display]
        public int? Grade { get; set; }

        /// <summary>Teacher/student signal - 5 = Teacher, 6 = Student. See ApplicationUser.Group's doc comment / docs/business/identity-and-access.md. Not the same concept as the Teacher/Student values on Role.</summary>
        [Display]
        public int? Group { get; set; }

        [Display]
        [FileSize(300 * 1024)] // 300KB
        [FileExtensions(Constants.ValidImageExtensions)]
        public IFormFile? Avatar { get; set; }

        [Display]
        public string? WalletId { get; set; }

        [Display]
        public string? Biography { get; set; }

        [Display]
        public IEnumerable<string?>? Skills { get; set; }

        [Display]
        public string? CurrentStatusSentence { get; set; }

        [Display]
        public string? Handle { get; set; }
    }
}
