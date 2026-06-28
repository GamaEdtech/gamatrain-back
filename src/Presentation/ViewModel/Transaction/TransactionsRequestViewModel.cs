namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public bool? IsDebit { get; set; }

        [Display]
        [JsonConverter(typeof(EnumerationConverter<TransactionType, short>))]
        public TransactionType? TransactionType { get; set; }
    }
}
