namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionsListResponseViewModel
    {
        public long Id { get; set; }

        public long UserId { get; set; }

        public long Points { get; set; }

        public string? Description { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public long CurrentBalance { get; set; }

        public bool IsDebit { get; set; }

        public long? IdentifierId { get; set; }

        [JsonConverter(typeof(EnumerationConverter<TransactionType, short>))]
        public TransactionType TransactionType { get; set; }
    }
}
