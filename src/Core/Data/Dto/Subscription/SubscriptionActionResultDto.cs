namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>
    /// Result of CancelSubscriptionAsync/ResumeSubscriptionAsync. Success is what the controller maps onto the
    /// public bool response; EmailNotification (set only when the action actually changed state) is what the
    /// controller - the only layer that references Hangfire - enqueues as a SendSubscriptionCancelled/ResumedEmailAsync
    /// background job, matching every other fire-and-forget email in this codebase (e.g. IdentitiesController.Register).
    /// </summary>
    public sealed class SubscriptionActionResultDto
    {
        public required bool Success { get; set; }
        public SubscriptionEmailRequestDto? EmailNotification { get; set; }
    }
}
