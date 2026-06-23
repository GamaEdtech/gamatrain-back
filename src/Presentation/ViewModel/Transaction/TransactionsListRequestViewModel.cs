namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionsListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public bool? IsDebit { get; set; }

        [Display]
        public long? UserId { get; set; }

        [Display]
        public string? Name { get; set; }

        [Display]
        public string? Email { get; set; }

        [Display]
        public long? IdentifierId { get; set; }

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<TransactionType, short>))]
        public TransactionType? TransactionType { get; set; }
    }
}
