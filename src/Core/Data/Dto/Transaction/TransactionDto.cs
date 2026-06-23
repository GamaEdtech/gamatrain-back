namespace GamaEdtech.Data.Dto.Transaction
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionDto
    {
        public long Id { get; set; }
        public long Points { get; set; }
        public string? Description { get; set; }
        public long CurrentBalance { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public bool IsDebit { get; set; }
        public long UserId { get; set; }
        public long? IdentifierId { get; set; }
        public TransactionType TransactionType { get; set; }
    }
}
