namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionsResponseViewModel
    {
        public long Id { get; set; }

        public long Points { get; set; }

        public string? Description { get; set; }

        public long CurrentBalance { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public bool IsDebit { get; set; }

        [JsonConverter(typeof(EnumerationConverter<TransactionType, short>))]
        public TransactionType TransactionType { get; set; }
    }
}
