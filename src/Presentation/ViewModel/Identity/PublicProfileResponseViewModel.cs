namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Experience;

    public sealed class PublicProfileResponseViewModel
    {
        public long ProfileView { get; set; }

        public string? AvatarUri { get; set; }

        public DateTimeOffset? RegistrationDate { get; set; }

        [JsonConverter(typeof(EnumerationConverter<OnlineStatus, byte>))]
        public OnlineStatus OnlineStatus { get; set; }

        public string? Biography { get; set; }

        public IEnumerable<string?>? Skills { get; set; }

        public string? CurrentStatusSentence { get; set; }

        public IEnumerable<ExperienceResponseViewModel>? Experiences { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        [JsonConverter(typeof(EnumerationConverter<UserRateLevel, byte>))]
        public UserRateLevel UserRateLevel { get; set; }

        public DateTimeOffset? OrphanDate { get; set; }
    }
}
