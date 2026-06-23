namespace GamaEdtech.Data.Dto.Transaction
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class CreateTransactionRequestDto
    {
        public required long UserId { get; set; }
        public long? IdentifierId { get; set; }
        public required long Points { get; set; }
        public required string? Description { get; set; }
        public required TransactionType TransactionType { get; set; }
    }
}
