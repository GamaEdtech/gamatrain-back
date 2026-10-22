namespace GamaEdtech.Data.Dto.Dashboard
{
    /// <summary>Teacher invite program report for a date range. Top teachers are all-time.</summary>
    public sealed class AdminReferralReportDto
    {
        public int OffersCreated { get; set; }

        public int OffersEmailed { get; set; }

        public int TeachersJoined { get; set; }

        public int InvitedSignUps { get; set; }

        /// <summary>Invited users with at least one referral commission row in the range.</summary>
        public int PayingInvitees { get; set; }

        public decimal EarnedUsd { get; set; }

        /// <summary>Invited sign-ups in the range flagged as a likely self-invite.</summary>
        public int SharedNetworkSignUps { get; set; }

        public IList<ReferralTeacherDto> TopTeachers { get; set; } = [];

        public IList<InvitedSignUpDto> RecentSignUps { get; set; } = [];
    }

    public sealed class ReferralTeacherDto
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

    public sealed class InvitedSignUpDto
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
