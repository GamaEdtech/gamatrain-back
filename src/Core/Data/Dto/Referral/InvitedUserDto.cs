namespace GamaEdtech.Data.Dto.Referral
{
    /// <summary>One user a teacher invited. Name only, shortened (no email/phone): the teacher may not know them.</summary>
    public sealed class InvitedUserDto
    {
        public string? DisplayName { get; set; }

        public DateTimeOffset? JoinedDate { get; set; }

        /// <summary>True while the user has an active subscription.</summary>
        public bool Subscribed { get; set; }

        public int PaymentsCount { get; set; }

        public decimal EarnedUsd { get; set; }
    }
}
