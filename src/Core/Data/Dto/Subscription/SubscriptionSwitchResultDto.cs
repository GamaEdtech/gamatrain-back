namespace GamaEdtech.Data.Dto.Subscription
{
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
    }
}
