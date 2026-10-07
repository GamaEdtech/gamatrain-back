namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class CommissionPayoutDto
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string? UserFirstName { get; set; }
        public string? UserLastName { get; set; }
        public decimal AmountUsd { get; set; }
        public string? Destination { get; set; }
        public PayoutStatus? Status { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public long? ApprovedByUserId { get; set; }
        public string? ApprovedByFullName { get; set; }
        public DateTimeOffset? ApprovalDate { get; set; }
        public long? PaidByUserId { get; set; }
        public string? PaidByFullName { get; set; }
        public DateTimeOffset? PaidDate { get; set; }
        public string? TransferReference { get; set; }
        public long? RejectedByUserId { get; set; }
        public string? RejectedByFullName { get; set; }
        public DateTimeOffset? RejectionDate { get; set; }
        public string? RejectionReason { get; set; }
        public DateTimeOffset? CancellationDate { get; set; }
    }
}
