namespace GamaEdtech.Presentation.ViewModel.UserActivity
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    /// <summary><c>POST activities</c>: a read of a post or a listen to its podcast.</summary>
    public sealed class RecordUserActivityRequestViewModel
    {
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<UserActivityType, byte>))]
        public UserActivityType? Type { get; set; }

        /// <summary>The post's id.</summary>
        [Display]
        [Required]
        public long? ContentId { get; set; }
    }
}
