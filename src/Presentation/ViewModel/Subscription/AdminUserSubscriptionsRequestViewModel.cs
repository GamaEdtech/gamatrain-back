namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class AdminUserSubscriptionsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public long? UserId { get; set; }

        /// <summary>Matches the subscriber's email, first name, last name or full name (contains).</summary>
        [Display]
        public string? Search { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<UserSubscriptionStatus, byte>))]
        public UserSubscriptionStatus? Status { get; set; }
    }
}
