namespace GamaEdtech.Presentation.ViewModel.Referral
{
    /// <summary>The caller's place in the teacher invite program. See docs/business/referrals.md.</summary>
    public sealed class ReferralStatusResponseViewModel
    {
        /// <summary>False for a non-teacher: they can't join or earn.</summary>
        public bool IsTeacher { get; set; }

        public bool Joined { get; set; }

        /// <summary>The percent locked at join, for life (joined only).</summary>
        public decimal? Percent { get; set; }

        /// <summary>The percent joining now would lock.</summary>
        public decimal JoinPercent { get; set; }

        /// <summary>The emailed offer's percent, when there was one.</summary>
        public decimal? OfferPercent { get; set; }

        public DateTimeOffset? OfferExpirationDate { get; set; }

        /// <summary>True while the emailed offer's percent can still be locked.</summary>
        public bool OfferOpen { get; set; }

        public DateTimeOffset? JoinedDate { get; set; }

        /// <summary>The code for invite links: any page URL + ?invite=CODE. Joined only.</summary>
        public string? ReferralCode { get; set; }

        public int InvitedCount { get; set; }

        public int SubscribedCount { get; set; }

        /// <summary>Lifetime referral commission in USD; already part of GET commissions/balance.</summary>
        public decimal EarnedUsd { get; set; }
    }
}
