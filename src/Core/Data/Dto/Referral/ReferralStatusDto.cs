namespace GamaEdtech.Data.Dto.Referral
{
    /// <summary>A teacher's view of the invite program (ReferralService.GetStatusAsync).</summary>
    public sealed class ReferralStatusDto
    {
        /// <summary>False for a non-teacher: they can't join or earn.</summary>
        public bool IsTeacher { get; set; }

        public bool Joined { get; set; }

        /// <summary>The percent locked at join (joined only).</summary>
        public decimal? Percent { get; set; }

        /// <summary>The percent joining now would lock: the offer's while it is open, otherwise the current setting.</summary>
        public decimal JoinPercent { get; set; }

        /// <summary>The emailed offer's percent, when there was one.</summary>
        public decimal? OfferPercent { get; set; }

        public DateTimeOffset? OfferExpirationDate { get; set; }

        /// <summary>True while the emailed offer's percent can still be locked.</summary>
        public bool OfferOpen { get; set; }

        public DateTimeOffset? JoinedDate { get; set; }

        /// <summary>The code to put in invite links (?invite=CODE). Joined only.</summary>
        public string? ReferralCode { get; set; }

        public int InvitedCount { get; set; }

        /// <summary>Invited users with at least one paid subscription payment.</summary>
        public int SubscribedCount { get; set; }

        /// <summary>Lifetime referral commission, USD. It is part of the commission balance (GET commissions/balance).</summary>
        public decimal EarnedUsd { get; set; }
    }
}
