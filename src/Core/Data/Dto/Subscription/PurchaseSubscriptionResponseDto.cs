namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PurchaseSubscriptionResponseDto
    {
        public long UserSubscriptionId { get; set; }

        /// <summary>0 (not a real id) when <see cref="Switched"/> - an immediate switch bills via a Stripe proration invoice, not this app's own Payment row.</summary>
        public long PaymentId { get; set; }

        /// <summary>Set only for a fresh purchase (redirect the caller here for Checkout) - null whenever <see cref="Switched"/> or <see cref="RequiresConfirmation"/>, since neither has anything to redirect to.</summary>
        public string? Url { get; set; }

        /// <summary>
        /// True when this call was delegated to a plan/interval switch on an already-existing subscription
        /// instead of starting a fresh purchase - the caller already has an Active recurring subscription, and
        /// what they asked for was achievable by changing it in place. See <c>PurchaseSubscriptionRequestDto.
        /// Confirm</c>.
        /// </summary>
        public bool Switched { get; set; }

        /// <summary>True when nothing was applied yet and this response is a preview - resubmit the identical request with Confirm = true to actually apply it and charge PreviewAmount. Only reachable when Switched would be true.</summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>Set only alongside RequiresConfirmation - the exact amount a Confirm = true resubmit will charge right now.</summary>
        public decimal? PreviewAmount { get; set; }

        /// <summary>Paired with PreviewAmount - null exactly when that is.</summary>
        public Currency? PreviewCurrency { get; set; }

        /// <summary>Internal only, never serialized to the client - set only on an actual switch state change, matching SwitchSubscriptionPlanAsync's own controller-enqueued email pattern.</summary>
        public SubscriptionEmailRequestDto? EmailNotification { get; set; }
    }
}
