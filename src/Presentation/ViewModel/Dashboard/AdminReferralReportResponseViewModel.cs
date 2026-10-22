namespace GamaEdtech.Presentation.ViewModel.Dashboard
{
    public sealed class AdminReferralReportResponseViewModel
    {
        public int OffersCreated { get; set; }

        public int OffersEmailed { get; set; }

        public int TeachersJoined { get; set; }

        public int InvitedSignUps { get; set; }

        public int PayingInvitees { get; set; }

        public decimal EarnedUsd { get; set; }

        public int SharedNetworkSignUps { get; set; }

        /// <summary>All-time, best first.</summary>
        public IEnumerable<ReferralTeacherResponseViewModel> TopTeachers { get; set; } = [];

        /// <summary>In the range, newest first.</summary>
        public IEnumerable<InvitedSignUpResponseViewModel> RecentSignUps { get; set; } = [];
    }

    public sealed class ReferralTeacherResponseViewModel
    {
        public long UserId { get; set; }

        public string? Name { get; set; }

        public DateTimeOffset? JoinedDate { get; set; }

        public decimal? Percent { get; set; }

        public int Invited { get; set; }

        public int Paying { get; set; }

        public decimal EarnedUsd { get; set; }

        public DateTimeOffset? LastInvitedDate { get; set; }
    }

    public sealed class InvitedSignUpResponseViewModel
    {
        public long UserId { get; set; }

        public string? Name { get; set; }

        public DateTimeOffset? SignedUpDate { get; set; }

        public long InviterUserId { get; set; }

        public string? InviterName { get; set; }

        public int Payments { get; set; }

        public decimal EarnedUsd { get; set; }

        /// <summary>Signed up from a network the inviting teacher was also seen on: a likely self-invite.</summary>
        public bool SharedNetwork { get; set; }
    }
}
