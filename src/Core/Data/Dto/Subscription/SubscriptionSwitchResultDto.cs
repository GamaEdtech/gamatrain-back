namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Result of SwitchSubscriptionPlanAsync. Success is what the controller maps onto the public response;
    /// Immediate/EffectiveDate tell the caller whether the switch (an upgrade) already happened or (a downgrade)
    /// is scheduled for the current period's end. EmailNotification (set only on an actual state change) is what
    /// the controller - the only layer that references Hangfire - enqueues as a
    /// SendSubscriptionSwitchedEmailAsync background job, matching CancelSubscriptionAsync/ResumeSubscriptionAsync's
    /// existing pattern.
    /// </summary>
    public sealed class SubscriptionSwitchResultDto
    {
        public required bool Success { get; set; }
        public bool Immediate { get; set; }
        public DateTimeOffset? EffectiveDate { get; set; }
        public SubscriptionEmailRequestDto? EmailNotification { get; set; }

        /// <summary>
        /// True when this call computed a preview instead of applying anything - <see cref="Success"/> is
        /// <see langword="false"/> in that case (nothing happened yet), but this isn't an error: resubmit the
        /// identical request with <c>Confirm = true</c> to actually apply it and charge <see cref="PreviewAmount"/>.
        /// </summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>Set only alongside <see cref="RequiresConfirmation"/> - the exact amount a Confirm=true resubmit will charge right now, from Stripe's own no-side-effect invoice-preview API.</summary>
        public decimal? PreviewAmount { get; set; }

        /// <summary>Paired with <see cref="PreviewAmount"/> - null exactly when that is.</summary>
        public Currency? PreviewCurrency { get; set; }
    }
}
