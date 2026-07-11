namespace GamaEdtech.Presentation.ViewModel.Connection
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ConnectionStatusRequestViewModel
    {
        [Display]
        [Required]
        public IEnumerable<long>? Ids { get; set; }

        /// <summary>
        /// Applies to every id in <see cref="Ids"/> - a single request can't mix local Ids and CoreIds. Defaults
        /// to Id when omitted.
        /// </summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<IdentifierType, byte>))]
        public IdentifierType? IdType { get; set; }
    }
}
