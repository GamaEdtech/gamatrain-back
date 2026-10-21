namespace GamaEdtech.Presentation.ViewModel.Referral
{
    /// <summary>One user the caller invited: a short name only, never contact details.</summary>
    public sealed class InvitedUserResponseViewModel
    {
        public string? DisplayName { get; set; }

        public DateTimeOffset? JoinedDate { get; set; }

        public bool Subscribed { get; set; }

        public int PaymentsCount { get; set; }

        public decimal EarnedUsd { get; set; }
    }
}
