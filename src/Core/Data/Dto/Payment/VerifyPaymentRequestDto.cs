namespace GamaEdtech.Data.Dto.Payment
{
    public sealed class VerifyPaymentRequestDto
    {
        public required long Id { get; set; }
        public required string? TransactionId { get; set; }

        /// <summary>
        /// The signed-in caller of the verify endpoint - the payment must be theirs. Null only for the server's own
        /// trusted paths (the signature-checked gateway webhook and the pending-payment sweep).
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// False for the pending-payment sweep: a checkout that isn't paid yet may still be paid, so it stays Pending
        /// instead of being closed as Failed. Its expiry is closed by the gateway's own expired webhook.
        /// </summary>
        public bool FailWhenUnpaid { get; set; } = true;
    }
}
