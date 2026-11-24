# Sale campaigns

Since 2026-11-24. A **campaign** is a time-boxed sale: Black Friday, Christmas, New Year, or any other event. While one is
live, a **new subscription's first payment** is a percent off, and the site shows the campaign's theme on the home hero
and in the plans dialogs. Admins create a campaign once, enable it with one click, and it starts and stops on its dates by
itself.

## What a campaign is

Table `Campaigns` (entity `Campaign`):

| Field | Meaning |
|---|---|
| `Title` | 1-60 characters, shown to users ("Black Friday") and in the Stripe coupon's name |
| `Headline` | Optional hero line (≤ 200); the frontend's theme text when empty |
| `Theme` | `CampaignTheme`: `Generic`, `BlackFriday`, `Christmas`, `NewYear`. The frontend draws each one; the backend only stores which |
| `DiscountPercent` | 1-90, off the first payment |
| `StartsAt` / `EndsAt` | The window, `[StartsAt, EndsAt)`, stored with their offset (UTC in practice) |
| `RepeatsYearly` | Comes back every year on the same days until disabled (see below) |
| `IsEnabled` | The one-click switch. A disabled campaign never applies, whatever its dates |

**Live** = enabled and now inside the current run (`CampaignRules.IsLive`). All the rules live in one place,
`src/Core/Data/Dto/Campaign/CampaignRules.cs`, so the checkout, the prices shown and the admin checks agree.

### Yearly campaigns

With `RepeatsYearly`, the stored dates are the first run's; each later run is the same window moved by whole years
(`CampaignRules.Occurrence`: the fewest years that leave its end after now). So a campaign entered as 24-28 Nov 2026 is
live 24-28 Nov 2027, 2028... with the same percent and theme, until an admin disables it. It follows fixed dates, not
"the fourth Friday of November": an admin who wants the exact Black Friday each year edits the dates. A yearly campaign
lasts at most 180 days, so one run always ends well before the next.

### Only one at a time

Two **enabled** campaigns may never be live at once: enabling one, or changing the dates of an enabled one, is refused
(`overlap`) when any of its runs, yearly ones included, would overlap another enabled campaign's
(`CampaignRules.OverlapsFrom`). Disabled campaigns can overlap freely (drafts for next year).

## What a live campaign does

### The discount (server-side only)

`SubscriptionService.PurchaseSubscriptionAsync` looks up the live campaign itself (`ICampaignService.GetCheckoutCampaignAsync`)
for every fresh purchase. **The client never sends a percent**, so it can't be faked. If the campaign ended a second before
the purchase, the purchase is at full price, whatever the page still showed.

- **Who gets it:** anyone with no `Active` subscription, new or returning. A purchase by a subscriber turns into a plan
  switch (see `subscriptions.md`), which is never discounted. The campaign never makes that path fail.
- **Stripe only:** the discount is a Stripe coupon on the checkout session, so other gateways charge full price.
- **First payment only:** the coupon is `duration = once`. `Payment.Amount` is the discounted first payment
  (`CheckoutRecoveryPricing.DiscountedPrice`, which rounds like Stripe). `UserSubscription.PricePaid` stays the full price,
  and renewals are charged in full.
- **Coupon:** `campaign-{campaignId}-{percent}-once`, named "{Title}: {percent}% off first payment" (cut to Stripe's 40
  characters), created the first time it is used. It is the same race-safe get-or-create as the recovery coupon
  (`StripePaymentGatewayProvider.GetOrCreateFirstPaymentCouponAsync`).
- **Recorded:** `Payments.CampaignId` names the campaign the payment's discount came from. The admin list shows each
  campaign's count of paid payments.
- **Quota is untouched:** the plan's feature limits never depend on what was paid, as with every discount (CLAUDE.md,
  "Subscription quota is never derived from payment amount").

### With a checkout recovery offer

A recovery offer (`subscriptions.md`, "Checkout recovery discount") and a live campaign **never stack**: the larger
discount wins (`CampaignRules.PickDiscount`). On a tie the recovery offer's coupon is used.

**Checkout recovery during a campaign.** While a live campaign gives at least the recovery percent, the recovery emails
are held (`NudgeService.SendCheckoutRecoveryNudgesAsync`). "20% off for you" next to a site-wide 30% reads wrong. They are
not lost: the job looks back 3 days, so a checkout left in a campaign's last days still gets its email once the campaign
ends. A campaign below the recovery percent doesn't hold them.

### Prices shown

The same lookup feeds the prices, so **the price shown is the price charged**:

- `GET subscriptions/plans`: each price carries `campaignPrice`, the first payment during the campaign. It is null when no
  campaign is live or the caller has an Active subscription.
- Upgrade suggestions (the refused download/export/spend responses): each price carries the same `campaignPrice`.

### The theme

`GET campaigns/current` (anonymous) returns the live campaign, else the next enabled one starting within 7 days
(`isLive: false`), else null: title, headline, theme, percent and the current run's `startsAt`/`endsAt`. It never returns
prices. Its dates (and the admin list's) are ISO 8601 UTC strings (`2026-11-27T00:00:00.0000000Z`), not the API's
usual `MM/dd/yyyy HH:mm:ss`, which is shifted to the caller's time zone with no zone in it: a countdown needs the real instant. It is cached for a minute. The frontend fetches it in the browser (never into server-rendered HTML, which the CDN
caches for hours) and switches the theme on at `startsAt` and off at `endsAt` with its own timers, so an open page changes
on time without a reload.

## Admin

`admin/campaigns` (Admin role only, **never delegable**: enabling one gives money away):

- **Create:** new campaigns start **disabled**.
- **Edit:** wording and theme are free. On an enabled campaign, changing the percent, dates or repeat needs the caller's
  authenticator code (CLAUDE.md, "Sensitive admin actions require a step-up authenticator code").
- **Enable:** needs the authenticator code and no overlap. It is refused for a one-off campaign that is already over.
  Enabling ahead of time is the normal way: create and enable on Thursday, and Black Friday starts by itself on Friday.
- **Disable:** the emergency stop. No code, and it takes effect at once.
- **Delete:** only a campaign no payment used. A used one stays for the record and can only be disabled.

The enabled campaigns are cached (10 minutes, `Campaigns:Enabled`) because every plans list and checkout reads them. Every
admin change clears the cache, and the window is always worked out against the current time on read, so a cached copy
never keeps a campaign alive past its end.

## Performance

- The backend adds one cached lookup per plans list and checkout, and nothing to any other request.
- `campaigns/current` is cached for 60 s.
- The frontend's effects (snow, fireworks, shimmer) load only for the live theme and only where shown. They pause off
  screen and are off for users who ask for reduced motion.
