namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class SwitchSubscriptionPlanResponseViewModel
    {
        public bool Success { get; set; }

        /// <summary>True if the switch applied right away (upgrade); false if it's deferred to EffectiveDate (downgrade).</summary>
        public bool Immediate { get; set; }

        public DateTimeOffset? EffectiveDate { get; set; }
    }
}
