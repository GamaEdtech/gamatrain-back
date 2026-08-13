# Subscriptions

Business logic: `src/Application/Service/SubscriptionService.cs` (plan/feature/price
definitions, purchase orchestration), `SubscriptionQuotaService.cs` (activation, quota
consumption, expiry). Contracts: `src/Application/Interface/ISubscriptionService.cs`,
`ISubscriptionQuotaService.cs`. Entities: `src/Domain/Entity/SubscriptionPlan.cs`,
`Feature.cs`, `SubscriptionPlanFeature.cs`, `SubscriptionPlanPrice.cs`,
`SubscriptionPlanGatewayMapping.cs`, `UserSubscription.cs`, `UserSubscriptionQuota.cs`,
`UserSubscriptionQuotaFeature.cs`.
See `docs/database/schema.md` for the column-level reference and
`docs/business/payments-and-points.md` for the points ledger / payment-gateway machinery
this feature builds on.

## The core idea: quota, not currency

A plan grants **fixed, named allowances per feature** — e.g. plan "Alpha" grants 500
pastpaper downloads, 100 test submissions, 100 exam participations for its billing
period. `Limit` is nullable end-to-end (`SubscriptionPlanFeature.Limit`,
`UserSubscriptionQuota.Limit`, and every DTO/ViewModel carrying it) — `NULL` means
**unlimited** for that feature, checked explicitly wherever `Limit` is compared or
subtracted (`SubscriptionQuotaService.ConsumeQuotaAsync`/`GetCurrentSubscriptionAsync`),
never via a sentinel like `int.MaxValue`. An unlimited plan feature always outranks a
finite `currentLimit` in `ConsumeQuotaAsync`'s upgrade suggestions, and sorts last (most
generous) rather than first. This is deliberately **not** a points top-up: buying a plan never runs the
amount paid through `ICurrencyConverterProvider`. A user in Turkey paying a
Turkey-priced amount and a user in the US paying a US-priced amount for the same plan
get *identical* quotas — the price is a regional lookup, the quota is a fixed property
of the plan. This keeps entitlement immune to exchange-rate movement (including the
GamaTrain gateway's on-chain/crypto rates).

Single-payment (points top-up) endpoints are unrelated and unchanged by this feature —
see `docs/business/payments-and-points.md`.

## Entities

- **`Feature`** — a data-driven catalog row (`Code`, `Name`, `Description`, `IsActive`),
  not an enum. Adding a new quota-limited action later is meant to be mostly data: insert
  a `Feature` row and reference it from `SubscriptionPlanFeature`. The one thing that
  still requires a code change is wiring the *consuming* call site to call
  `ISubscriptionQuotaService.ConsumeQuotaAsync` with the new `Code` — the catalog and
  limits are free, the enforcement hook at the point of use is not.
  Seeded codes (`src/Domain/Enumeration/FeatureCodes.cs`): `PastpaperDownload`,
  `TestDownload` (both wired, see below), `TestSubmission`, `ExamParticipation` (seeded
  `IsActive = false` — cataloged for future use, no call site charges them yet).
  `SubscriptionService.ManageFeatureAsync` rejects (`NotValid`) any `Code` that isn't one
  of these constants, so a typo'd/made-up code can't be saved as a `Feature` that then
  never gets enforced by anything. `GET admin/subscriptions/features/codes`
  (`SubscriptionService.GetFeatureCodes`, reflects over the `FeatureCodes` constants) is
  the source for a front-end `Code` dropdown, rather than a free-text input — it's
  compile-time data, not DB-backed, so it doesn't take a request DTO.
- **`SubscriptionPlan`** — the *product*: `Title`, `IsActive`, `Highlight`, `Polygon` (geo
  region, settable via the admin API; currently **not enforced** — `GET /api/v1/plans`
  lists every active plan globally regardless of the caller's location or a plan's
  `Polygon`). Carries **no price and no billing interval** — both live on
  `SubscriptionPlanPrice` (see below), mirroring how Stripe/PayPal model one Product with
  several Prices that vary by interval and currency, rather than baking the interval into
  the product itself. This is a deliberate redesign (2026-08-03): `BillingInterval`
  originally lived here, which accidentally scoped a whole Plan to one interval — a "Pro
  Monthly" and "Pro Yearly" had to be two disconnected `SubscriptionPlan` rows with no
  relationship, breaking any honest Monthly-vs-Yearly comparison in upgrade-suggestion
  UX. Moving it to `SubscriptionPlanPrice` means Monthly and Yearly are just sibling
  prices under the same plan — no linking table needed.
- **`SubscriptionPlanFeature`** — `(SubscriptionPlanId, FeatureId, Limit, FeatureGroupKey,
  FeatureGroupDescription)`. One row per feature a plan grants. Quota is plan-wide, not
  price-wide: buying the Yearly variant of a plan grants the exact same per-feature limits
  as the Monthly variant, just for a longer period — consistent with quota never being
  derived from price/payment amount. `FeatureGroupKey` (nullable string, added
  2026-08-03) is how two or more features on the same plan **pool onto one shared quota**
  instead of each getting an independent one — e.g. `ExamDownload` and
  `PastpaperDownload` both drawing down the same 500-unit bucket. It's set via
  `SetPlanFeaturesAsync`'s request shape, `FeatureGroups: [{ FeatureIds: [...], Limit,
  Description }]` — admin/caller expresses pooling by putting feature ids in the same
  array entry, never by inventing/matching a string themselves; the key itself is a
  **server-generated GUID**, purely a DB linking detail invisible to the API (a group
  with one `FeatureId` gets `FeatureGroupKey = NULL`, today's unpooled behavior).
  `Description` is **required whenever a group pools 2+ features** (`NotValid` /
  `FeatureGroupDescriptionRequired` otherwise) — a pooled bucket has no single feature to
  describe it, so unlike a single-feature entry (which already has `Feature.Description`
  to show), the group needs its own; it's ignored for a single-feature entry.
  `GetPlanFeaturesAsync` surfaces pooling back as `PlanFeatureGroupDto` — one entry per
  bucket, `Features: [{ FeatureId, FeatureCode, FeatureName }, ...]` (one entry when
  unpooled, several when pooled) plus a single `Limit`/`Description` shared by the whole
  group (`Description` already resolved: the pool's when pooled, otherwise the single
  feature's own `Feature.Description`). This mirrors `SetPlanFeaturesAsync`'s write shape
  exactly (`FeatureGroups: [{ FeatureIds, Limit, Description }]`) so a pooled group is one
  entry with N feature codes inside it, never N entries repeating the same limit and
  description (2026-08-04 fix — it originally stayed flat, one row per feature, with a
  `PooledFeatureCodes` list bolted on to point at siblings, which just meant the group's
  `Limit`/`Description` were duplicated once per feature in the group instead of stated
  once).
- **`SubscriptionPlanPrice`** — `(SubscriptionPlanId, CountryCode, Currency, Price,
  BillingInterval)`. `CountryCode = NULL` is the **global default** price for that
  interval, and a unique index on `(SubscriptionPlanId, CountryCode, BillingInterval)`
  guarantees at most one row per plan+country+interval combination (SQL Server treats
  `NULL` as a distinct value in unique indexes, so this doesn't collide across rows). A
  single plan can now have several price rows — one per `BillingInterval`
  (Daily/Weekly/Monthly/Seasonally/Yearly) it's offered at, each with its own default
  (and, once regional pricing is enabled, per-country) price — regional pricing is built
  but dormant, see below.
- **`SubscriptionPlanGatewayMapping`** — `(SubscriptionPlanPriceId, Gateway,
  ExternalProductId, ExternalPlanId)`. Keyed off the *price* row, not the plan, because
  gateway Product/Price objects (Stripe Prices, PayPal Plans) are currency- **and**
  interval-bound — a Turkey-TRY-Monthly price, a US-USD-Monthly price, and a US-USD-Yearly
  price of the same plan each need their own external id, and since `BillingInterval` now
  also lives on `SubscriptionPlanPrice`, this table needed no change to already support
  that. This table is written by admin today but **not yet read by anything** — it's
  reserved for a later native-recurring-billing phase (Stripe Subscriptions/webhooks); the
  current purchase flow is one-time checkout and doesn't need it.
- **`UserSubscription`** — one purchase/enrollment: `UserId`, `SubscriptionPlanId`,
  `Status` (`Pending`/`Active`/`Expired`/`Cancelled`), `CreationDate`, `StartDate`/
  `ExpirationDate` (set on activation), `PricePaid`/`Currency`/`BillingInterval`
  (snapshotted at purchase, from the resolved `SubscriptionPlanPrice` — a later admin
  price edit never changes what an existing subscriber already paid or which interval
  they're on). The `BillingInterval` snapshot is what `ActivateSubscriptionAsync` uses to
  compute `ExpirationDate` — it's read directly off the `UserSubscription` row, not looked
  up through the plan, since a plan alone no longer identifies a single interval. The link
  to the payment that paid for it is **`Payment.UserSubscriptionId`, not the reverse** —
  `UserSubscription` has no `PaymentId` column, which is what avoids a circular FK between
  the two tables.
- **`UserSubscriptionQuota`** — one **bucket** per `UserSubscription` (not per feature
  anymore): `Limit` (snapshotted from the group's `SubscriptionPlanFeature.Limit` at
  activation time) and `Used`. `Description` is also snapshotted at activation, already
  resolved the same way `PlanFeatureGroupDto.Description` is — the group's
  `FeatureGroupDescription` when the bucket covers 2+ features, otherwise the single
  feature's own `Feature.Description` — so it's always populated, never `NULL`.
  `Remaining` is always computed (`Limit - Used`), never stored, so there's only one
  number to keep consistent under concurrent decrements. Which
  feature(s) a bucket covers lives in the child `UserSubscriptionQuotaFeature` table
  (`UserSubscriptionQuotaId`, `UserSubscriptionId` denormalized purely so a unique index
  on `(UserSubscriptionId, FeatureId)` can guarantee one bucket per feature per
  subscription, `FeatureId`) — usually one row (an unpooled feature), more than one when
  `SubscriptionPlanFeature.FeatureGroupKey` pooled several features at activation. At the
  API layer (`GET subscriptions/me`), `UserSubscriptionQuotaFeatureDto.Description`
  doesn't read `Feature.Description` directly — it repeats the parent bucket's own
  already-resolved `Description`, so every row in a quota's feature list carries the same
  one-description-per-row shape as an upgrade suggestion's feature list, rather than
  mixing a resolved bucket-level description with raw per-feature ones.
  `ConsumeQuotaAsync` matches a `featureCode` via `q.Features.Any(f => f.Feature.Code ==
  featureCode)` instead of a direct column, but the guarded-`UPDATE` decrement itself is
  unchanged — it still targets a single bucket row by `Id`, so pooling doesn't change the
  concurrency-safety story at all.

## Regional pricing: built now, dormant until a config flag flips

`Subscription:RegionalPricingEnabled` (`appsettings.json`, default `false`) gates
`SubscriptionService.ResolvePriceAsync`:

- **Flag off** (today): always returns the plan's default (`CountryCode = NULL`) price
  row, regardless of the caller's location.
- **Flag on**: resolves the caller's country server-side (from `ApplicationUser.City` →
  `Location.Parent` (state) → `Location.Parent` (country) → `Code`) and looks for a
  matching `SubscriptionPlanPrice` row, falling back to the default row if none exists
  for that country. **The client never sends a country code or amount** — resolution is
  entirely server-driven so a request can't just claim a cheaper region.

Turning the flag on with no country-specific price rows yet inserted is safe — every
plan still resolves to its default row via the fallback. Adding "Alpha priced in TRY for
Turkey" later is purely a data change: insert one `SubscriptionPlanPrice` row (and, once
the recurring-billing phase exists, a matching `SubscriptionPlanGatewayMapping` row) —
no code or schema change required.

## Plan visibility: geo-fence removed, USD-everywhere for now

`SubscriptionsController.GetSubscriptionsList` (`GET api/v1/plans`) used to resolve the
caller's coordinate (`IIdentityService.GetUserCoordinateAsync`, from `ApplicationUser.City`)
and filter plans by whether that point fell inside the plan's `Polygon`
(`CoordinateInsideSpecification`) — a user with no `City` set got an empty list back, even
for plans with no `Polygon` at all. This has been removed: the endpoint now lists every
active plan unconditionally (`ActiveSpecification` only), matching the current
USD-everywhere/no-regional-pricing rollout stage described above. The `Polygon` column
and the admin API to set it (`SubscriptionsController` in `Areas/Admin`) are unchanged —
re-adding enforcement later is purely restoring the `.And(new
CoordinateInsideSpecification(...))` filter, no schema change required.

## Purchase → verify → activate lifecycle

1. **`SubscriptionService.PurchaseSubscriptionAsync`** (`POST
   api/v1/subscriptions/plans/{id}/purchase`): the request body now requires
   `BillingInterval` alongside `Gateway` — since a plan can offer more than one interval,
   the client has to say which one it wants; the endpoint still never trusts a
   client-supplied price, only which interval to resolve. Validates the plan is active,
   resolves its price via `ResolvePriceAsync` (now filtered on plan + country +
   `BillingInterval`), inserts a `UserSubscription` row (`Status = Pending`,
   `PricePaid`/`Currency`/`BillingInterval` snapshotted from the resolved price), then calls the existing
   `IPaymentService.CreatePaymentAsync` with `UserSubscriptionId` set on the request —
   this reuses the exact same gateway-checkout mechanics as an ordinary top-up (Stripe
   Checkout Session, GamaTrain wallet), just tagged with which subscription it's for. If
   the gateway call fails, the `UserSubscription` is flipped `Pending → Cancelled` via a
   guarded set-based update; a `Pending` row that never gets a follow-up verify call is
   harmless and simply never activates.
2. **`PaymentService.VerifyPaymentAsync`** (unchanged public route, `POST
   api/v1/payments/{id}/verify`): branches on `Payment.UserSubscriptionId`. When set, it
   skips the points-conversion/credit path entirely and instead calls
   `ISubscriptionQuotaService.ActivateSubscriptionAsync` inside the same
   `TransactionScope` used for the payment-status update — see
   `docs/business/payments-and-points.md` for the shared mechanics.
3. **`SubscriptionQuotaService.ActivateSubscriptionAsync`**: computes
   `StartDate = now`, `ExpirationDate = subscription.BillingInterval.CalculateEndDate(start)`
   — reading `BillingInterval` straight off the `UserSubscription` row's own snapshot
   (no join to `SubscriptionPlan` needed, unlike before this was moved off the plan),
   then does a **guarded** set-based
   update (`WHERE Status == Pending`) to flip the subscription to `Active` — zero rows
   affected means this activation already happened (e.g. a duplicate verify call), and
   the method fails cleanly without double-activating. It then groups the plan's active
   `SubscriptionPlanFeature` rows by `FeatureGroupKey` (a `NULL` key is its own singleton
   group — an unpooled feature) and snapshots one `UserSubscriptionQuota` bucket per
   group, with one `UserSubscriptionQuotaFeature` child row per feature in that group.

## Native recurring billing (Stripe)

Built 2026-08-10, gateway-parameterized so a future gateway (PayPal) is purely additive. GamaTrain
(crypto wallet) never implements this — it has no saved-payment-method/auto-charge concept — so its
purchases stay one-time checkout exactly as before, unconditionally.

- **Auto-renew by default, no opt-in.** Any Stripe purchase of a subscription becomes a real Stripe
  Subscription (Checkout `Mode = "subscription"`), not a one-time charge — `PaymentService.
  CreatePaymentAsync` branches on whether `IGenericFactory<IRecurringPaymentGatewayProvider,
  PaymentGateway>.GetProvider(requestDto.Gateway)` returns a provider **and** a
  `SubscriptionPlanPriceId` was supplied (only `SubscriptionService.PurchaseSubscriptionAsync` ever
  sets it) — a plain points top-up never takes this path, on any gateway.
- **`SubscriptionPlanGatewayMapping` finally gets read.** The recurring-checkout path resolves the
  Stripe recurring Price id from that table (keyed by `SubscriptionPlanPriceId` + `Gateway`) and
  fails cleanly (`RecurringGatewayMappingMissing`) if no mapping is registered for that plan price —
  it never falls back to an inline, unregistered price. An admin must create the real Stripe
  Product/Price out-of-band and register the mapping (`POST admin/subscriptions/gateway-mappings`,
  already existed) before a plan can be sold with recurring billing.
- **First activation is unchanged**: still the client-driven `POST payments/{id}/verify` →
  `ActivateSubscriptionAsync` path (idempotent, guarded on `Status == Pending`).
- **Renewal is webhook-driven**: `POST payments/webhooks/{gateway}` (`[AllowAnonymous]`,
  `PaymentsController.RecurringWebhook`) receives Stripe's `invoice.paid`/
  `customer.subscription.deleted` events. `IRecurringPaymentGatewayProvider.ParseWebhookEventAsync`
  (Stripe: `StripePaymentGatewayProvider`) reads the raw body/`Stripe-Signature` header itself
  (mirroring `ResendEmailProvider.ProccessInboundEmailAsync`'s inbound-webhook handling) and verifies
  the signature via `Stripe.EventUtility.ConstructEvent` against `PaymentGateway:Stripe:
  WebhookSecret` — never trusts an unverified payload. It's pure parsing, no DB access; the caller
  (`PaymentService.HandleRecurringWebhookAsync`) does all persistence.
  **Operational note, found during verification**: `ConstructEvent` defaults
  `throwOnApiVersionMismatch: true` (never overridden here) and rejects an event whose
  `api_version` doesn't match what the installed Stripe.net version expects (52.0.0 expects
  `2026-05-27.dahlia`) — indistinguishable from a bad signature in the returned result (both
  surface as `StripeException` → `RecurringWebhookEventDto` failure). Whoever registers the real
  Stripe webhook endpoint (Dashboard or API) must pin its API version to match the deployed
  Stripe.net version, or every real webhook will be silently rejected. This is deliberately not
  relaxed to `throwOnApiVersionMismatch: false` — the SDK's own warning ("objects may be
  incorrectly deserialized") is a real financial-correctness risk, not just noise.
- **No new column for correlating a webhook event back to a `UserSubscription`.**
  `SessionCreateOptions.SubscriptionData.Metadata["userSubscriptionId"]` is set at checkout, and
  Stripe carries that metadata through to every invoice under the created Subscription
  (`Invoice.Parent.SubscriptionDetails.Metadata`) — read directly off the webhook payload, no
  separate fetch of the Subscription object needed.
- **`SubscriptionQuotaService.RenewSubscriptionAsync`** (idempotent, no-op unless `Status ==
  Active`): extends `ExpirationDate` one more `BillingInterval` **from the subscription's own
  current `ExpirationDate`**, not "now" — keeps the cycle anchored even if the webhook runs a little
  late — then resets every `UserSubscriptionQuota.Used` for that subscription back to `0`. The
  **same `UserSubscription` row keeps renewing** rather than a new row per period (schema already
  supported this: `Payment.UserSubscriptionId`/`UserSubscription.Payments` already allow many
  payments per subscription).
- **Idempotency against webhook redelivery** reuses `Payment`'s existing unique index
  `(TransactionId, Gateway)` — a renewal's `Payment.TransactionId` is the Stripe invoice id; a
  redelivered event hits the unique constraint on insert (`UniqueConstraintException`, same pattern
  as `IdentityService`/`TransactionService`/`LocationService`), and critically the renewal call
  itself is skipped too on a duplicate, not just the insert — otherwise a redelivered event would
  extend `ExpirationDate` twice for the same period.
- **`SubscriptionQuotaService.CancelSubscriptionAsync`** (guarded, `Active → Cancelled`): driven by
  Stripe's own `customer.subscription.deleted` event — i.e. Stripe's built-in Smart Retries were
  exhausted, or the subscription was cancelled Stripe-side, **or** the real period end was reached
  after a user requested cancellation (see "User-facing subscription cancellation" below) — this
  method itself doesn't distinguish why, it just reacts to the gateway telling us a subscription
  ended.
- **Dunning is entirely Stripe's**, not hand-rolled: this integration never implements its own
  retry/grace-period logic for a failed renewal charge, relying on Stripe Smart Retries and just
  reacting to the terminal `customer.subscription.deleted` event.
- **Interaction with "Expiry: forfeiture, not clawback" above**: for a Stripe-recurring subscription
  under normal operation, `RenewSubscriptionAsync` keeps pushing `ExpirationDate` forward faster
  than it can lapse, so the lazy/batch expiry path is mostly a safety net — e.g. if a webhook
  delivery was somehow missed entirely, quota still stops being usable at the old `ExpirationDate`
  rather than silently staying valid forever on faith that a renewal happened.

## User-facing subscription cancellation

Built 2026-08-11 (issue #536), on top of native recurring billing. **Cancels at period end, not
immediately** — the user keeps quota/access until their current paid period's `ExpirationDate`, no
refund needed since they already paid for it.

- **`POST subscriptions/me/cancel`**: action-style, matching `plans/{id}/purchase` — cancels the
  caller's own current subscription, resolved from the authenticated user the same way `GET
  subscriptions/me` already does. No id in the request.
- **The real gap this closed: nothing previously stored the gateway's own recurring-subscription id.**
  `SessionCreateOptions.SubscriptionData.Metadata` carries *our* `userSubscriptionId` *to* Stripe, but
  nothing carried Stripe's own Subscription id (`sub_...`) back *into* the DB — the one place that'd
  naturally appear (a renewal `invoice.paid`'s `Parent.SubscriptionDetails.Subscription`) is
  deliberately skipped for the first invoice (see the double-extension fix above), and a user should
  be able to cancel before their first renewal anyway. Fixed by reading it where it's already
  available for free: `Stripe.Checkout.Session.SubscriptionId` is present on the session the moment
  `VerifyAsync` confirms payment, no extra Stripe call needed.
- **Two new `UserSubscription` columns** (migration `AddSubscriptionCancellationFields`):
  - `ExternalSubscriptionId` (`string?`) — captured by `StripePaymentGatewayProvider.VerifyAsync` →
    `VerifyResponseDto.ExternalSubscriptionId` → `ActivateUserSubscriptionRequestDto` →
    `ActivateSubscriptionAsync`'s existing guarded update. `NULL` for a one-time/GamaTrain
    subscription, or a Stripe subscription that hasn't finished activating yet — doubles as the "is
    this actually recurring" signal, exposed to clients as `AutoRenews` (`= ExternalSubscriptionId is
    not null`) on `GET subscriptions/me`, closing the earlier gap where a client had no way to tell a
    Stripe-recurring subscription from a one-time GamaTrain one.
  - `CancelAtPeriodEnd` (`bool`) — set by `SubscriptionQuotaService.RequestCancellationAsync`
    (guarded on `Active`, idempotent) when the user requests cancellation. Deliberately doesn't touch
    `Status`/`ExpirationDate` itself — those change later, when Stripe's own
    `customer.subscription.deleted` fires at the real period end and the existing (unchanged)
    webhook → `CancelSubscriptionAsync` path flips it `Cancelled`, exactly like it already does for
    any other subscription-ended reason. Also exposed on `GET subscriptions/me`.
- **`ISubscriptionService.CancelSubscriptionAsync(userId)`** orchestrates: look up the current
  `Active` subscription (plus its `ExternalSubscriptionId` and, via any one linked `Payment`, its
  `Gateway` — the gateway never changes mid-subscription, so this avoids a separate `Gateway` column
  on `UserSubscription` itself) →
  - not found → `UserSubscriptionNotFound`;
  - `ExternalSubscriptionId is null` → `NotValid`/`SubscriptionNotRecurring` — a one-time/GamaTrain
    subscription was never going to renew, so there's nothing to cancel (deliberately not a silent
    no-op or an early revoke either — it already stops at its own `ExpirationDate` by design);
  - already `CancelAtPeriodEnd` → `Succeeded` no-op (idempotent, doesn't re-call the gateway);
  - otherwise resolves `IGenericFactory<IRecurringPaymentGatewayProvider, PaymentGateway>` for that
    plan's gateway and calls its new `CancelSubscriptionAsync(externalSubscriptionId)` — Stripe:
    `SubscriptionService().UpdateAsync(id, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true
    })`, Stripe's own cancel-at-period-end primitive, so Stripe keeps tracking the exact end date and
    still fires `customer.subscription.deleted` at the real period end unchanged. Only sets the local
    flag if the gateway call actually succeeded, to stay consistent with Stripe's real state.
- **Gateway-agnostic by construction**, same as the rest of native recurring billing: adding PayPal
  later needs no changes here either, just a `PayPalRecurringPaymentGatewayProvider` implementing
  `CancelSubscriptionAsync`.

### Resuming a pending cancellation

- **`POST subscriptions/me/resume`**: exact mirror of `me/cancel` — reverses a pending
  `CancelAtPeriodEnd` request for the caller's own current active subscription, any time before the
  real period end still arrives. Same `NotFound`/`SubscriptionNotRecurring` cases as cancel; idempotent
  no-op (`Succeeded`) if nothing was pending.
- `ISubscriptionService.ResumeSubscriptionAsync(userId)` mirrors `CancelSubscriptionAsync`'s structure:
  resolves the recurring gateway provider and calls its new `ResumeSubscriptionAsync(externalSubscriptionId)`
  — Stripe: `SubscriptionService().UpdateAsync(id, new SubscriptionUpdateOptions { CancelAtPeriodEnd =
  false })` — then, only on success, `SubscriptionQuotaService.ResumeSubscriptionAsync` clears the local
  `CancelAtPeriodEnd` flag (guarded on `Active`).

### Email notifications

Both actions send a fire-and-forget confirmation email, following the same pattern already used for
registration/ticket/contribution emails elsewhere in the codebase: **the Hangfire `BackgroundJob.Enqueue`
call lives in `SubscriptionsController`** (the only layer in this codebase that references Hangfire —
`Application.Service` deliberately does not), not in `SubscriptionService`. To make that possible without
the service reaching for Hangfire itself, `CancelSubscriptionAsync`/`ResumeSubscriptionAsync` return
`SubscriptionActionResultDto { Success, EmailNotification }` instead of a bare `bool`: `EmailNotification`
(a `SubscriptionEmailRequestDto` with `UserId`/`PlanTitle`/`ExpirationDate`) is populated only when the
action actually changed state — not on the idempotent no-op paths — and the controller enqueues
`ISubscriptionService.SendSubscriptionCancelledEmailAsync`/`SendSubscriptionResumedEmailAsync` when it's
present, then maps `Success` onto the public `data: bool` response so the wire contract is unchanged.

Templates are two new `ApplicationSettingsDto` string properties (admin-editable, same as every other
`*EmailTemplate` setting): `SubscriptionCancelledEmailTemplate` and `SubscriptionResumedEmailTemplate`,
supporting the standard `[RECEIVER_NAME]`/`[PLAN_TITLE]`/`[DATE]` placeholder tokens (`[DATE]` = the
subscription's `ExpirationDate` — i.e. when access actually ends, or when it resumes auto-renewing).

## Admin visibility/management of user subscriptions

Built 2026-08-12. Before this, the admin `SubscriptionsController` only managed the catalog
(plans/features/prices/gateway-mappings) — there was no way to look up or list a *user's* subscription(s),
or to manually grant/revoke/extend one for a support case. New endpoints, all under
`api/v1/admin/subscriptions/users`:

- **`GET users`** (paged, filterable by `userId`/`status`) and **`GET users/{id}`** — read-only visibility
  into any user's subscription(s), including fields never exposed on the self-service `subscriptions/me`
  (the raw `externalSubscriptionId`, and which `Gateway` was used) since a support case needs both.
- **`POST users/grant`** (`userId`, `subscriptionPlanId`, `billingInterval`) — a comped subscription for a
  support case. Bypasses the normal `Pending` → `Payment` → `VerifyAsync` → `Activate` flow entirely: the
  `UserSubscription` row is created `Active` immediately, `PricePaid = 0`, `Currency = USD`, and its quota
  buckets are snapshotted the same way `ActivateSubscriptionAsync` does for a real purchase (the
  bucket-snapshotting logic is shared between the two via a private `CreateQuotasAsync` helper).
- **`POST users/{id}/revoke`** — immediate revocation, distinct from the user-facing cancel-at-period-end
  flow (`subscriptions/me/cancel`): if the subscription is gateway-recurring (`ExternalSubscriptionId` set),
  it's terminated gateway-side *first* via a new `IRecurringPaymentGatewayProvider.TerminateSubscriptionAsync`
  — Stripe: `SubscriptionService().CancelAsync(id)`, which cancels and stops billing immediately, unlike the
  existing `CancelSubscriptionAsync`'s `CancelAtPeriodEnd = true` — then the local row flips `Cancelled`
  right away via the existing (webhook-driven) `SubscriptionQuotaService.CancelSubscriptionAsync`. Access
  stops immediately, not at period end.
- **`POST users/{id}/extend`** (`days`) — pushes `ExpirationDate` forward by the given number of days for a
  support case. Local record only — never re-bills or touches the gateway side, so it has no effect on when
  Stripe's own recurring billing next charges a gateway-recurring subscription.

## Plan upgrade/downgrade with proration

Built 2026-08-12 (issue #554), on top of native recurring billing and cancellation. Before this there was no
way to switch plans at all — buying a second plan while one was already Active never had a coherent policy
(quota consumption already anticipated stacking, draining the earliest-expiring subscription first, but
switching itself was unbuilt).

- **`POST subscriptions/me/switch`** (`subscriptionPlanId`) — single endpoint, Stripe-recurring only (same
  `SubscriptionNotRecurring` rejection as `me/cancel` for a one-time/GamaTrain subscription). The backend
  decides upgrade vs. downgrade itself by comparing the target plan's resolved price (`ResolvePriceAsync`, same
  `BillingInterval` as today — interval never changes as part of a switch, only plan/tier does) against the
  current subscription's own `PricePaid`. Equal price is treated as a downgrade — no additional payment is
  being taken, so there's no forfeited-value reason to apply it immediately. Guards, in order: no current
  subscription (`UserSubscriptionNotFound`), non-recurring (`SubscriptionNotRecurring`), same plan
  (`SamePlanSwitchNotAllowed`), target plan inactive (`PlanNotAvailable`), a cancellation already pending
  (`SwitchNotAllowedWhileCancellationPending` — resume first), cross-currency (`SwitchCurrencyMismatch`, only
  reachable once regional pricing goes live).
- **Upgrade** (target price beats current `PricePaid`): applies **immediately**. Stripe:
  `SubscriptionService().UpdateAsync(id, new SubscriptionUpdateOptions { Items = [...], ProrationBehavior =
  "always_invoice" })` — swaps the item's price and invoices the prorated difference right away. Locally,
  `SubscriptionPlanId`/`PricePaid` update immediately and quota buckets are re-snapshotted fresh for the new
  plan via `CreateQuotasAsync` (see below).
- **Downgrade** (target price ≤ current): deferred to the **end of the current billing period** — no
  proration/credit math needed, mirrors `CancelAtPeriodEnd`'s "keep what you have until period end" UX. A bare
  `ProrationBehavior=none` item update does **not** achieve this — verified against Stripe's own docs, it still
  applies the new price at the moment of the call, it only skips generating a proration invoice line. The
  correct, documented mechanism is a **Subscription Schedule**:
  `SubscriptionScheduleService().CreateAsync(new() { FromSubscription = externalSubscriptionId })` converts
  the plain subscription into a 1-phase schedule mirroring its current state through the current period end,
  then `UpdateAsync` sets 2 phases — phase 0 keeps the existing price through that period end (copied from
  what `CreateAsync` returned), phase 1 (no `EndDate`) starts the new price, `EndBehavior = "release"` so once
  phase 1 completes one cycle, Stripe hands the subscription back to ordinary plain auto-renewal on the new
  price — no schedule involved for any renewal after that. Stripe flips the item at the phase boundary itself,
  *before* generating that period's invoice, so the existing `invoice.paid` webhook handling already reflects
  the new, lower charge correctly with no changes needed there.
- **Two new nullable `UserSubscription` columns** (migration `AddSubscriptionPlanSwitchFields`):
  `PendingSwitchSubscriptionPlanId` and `PendingSwitchPricePaid` (snapshotted at request time, not re-resolved
  at renewal — must match whatever price the Subscription Schedule already locked in). No
  `PendingSwitchCurrency` column — a switch is only ever accepted when it's already same-currency as the
  current subscription (`SwitchCurrencyMismatch` above), so nothing new to track there. `RenewSubscriptionAsync`
  checks for these at the renewal boundary: if set, it swaps `SubscriptionPlanId`/`PricePaid`/`ExpirationDate`
  and re-snapshots quotas for the new plan instead of just extending the current plan's `ExpirationDate` and
  resetting `Used` to 0. `RequestCancellationAsync` clears both fields when cancellation is requested — a
  pending downgrade doesn't make sense once the subscription is ending anyway; cancellation always wins.
- **`CreateQuotasAsync` (shared by `ActivateSubscriptionAsync`/`GrantSubscriptionAsync`/the switch flow) now
  deletes existing quota rows before inserting** — the two original callers only ever ran it against a
  brand-new subscription with no prior quota rows, so this is a no-op for them; it's required for a plan
  switch reusing an existing subscription, since old and new plan sharing a `FeatureId` would otherwise violate
  `UserSubscriptionQuotaFeature`'s unique index.
- **`CancelSubscriptionAsync`/`TerminateSubscriptionAsync` release/cancel any attached Schedule first** —
  cancellation overrides a pending downgrade, consistent with clearing the local pending fields above.
  **`ResumeSubscriptionAsync` deliberately does *not*** — by the time a cancellation exists to resume,
  `CancelSubscriptionAsync` already released any schedule that was there; a schedule found during a resume call
  instead means a downgrade is separately pending and must be left alone. (An earlier version of this code
  released the schedule unconditionally in `ResumeSubscriptionAsync` too — caught during live verification
  against real Stripe test-mode objects: it would silently destroy a legitimately-pending downgrade the moment
  `me/resume` was called for any unrelated reason, without touching the local pending-switch fields, leaving
  local and Stripe state permanently out of sync. Fixed before merge.)
- **Known, accepted limitation**: upgrade/downgrade is decided by comparing plan *price* only, not actual
  feature limits — a differently-priced plan could theoretically be worse on the one feature a user cares
  about. Accepted for v1, matches the issue's own guidance to keep this simple.
- **A pending downgrade is exposed on `GET subscriptions/me`** (and the admin `GET
  users`/`users/{id}` equivalent) as `pendingSwitchPlanId`/`pendingSwitchPlanTitle` — both `null` when
  nothing's pending. No separate effective-date field: a pending switch always takes effect at the
  subscription's own `expirationDate`, already present on the same response. Added 2026-08-12, right
  after the initial ship, once the frontend work (Trello) surfaced needing it for an account-page status
  badge ("Switching to [Plan] on [date]").

## Self-service subscription history

Built 2026-08-13. `GET subscriptions/me` only ever surfaces the caller's *current* subscription
(the one row selected by `SubscriptionQuotaService.GetCurrentSubscriptionAsync`) - there was
previously no self-service way to see past ones, only the admin `GET admin/subscriptions/users`
listing (which additionally exposes `UserId`/`UserEmail`/`ExternalSubscriptionId`/`Gateway`, none
of which belong on a caller-scoped response).

- **`GET subscriptions/me/history`** (paged, newest first via the default `Id desc` sort baked
  into `FilterListAsync`) - the caller's own `UserSubscription` rows with `Status` in `Expired` or
  `Cancelled`. `Pending`/`Active` are deliberately excluded: `Pending` never finished a purchase
  and `Active` is already `GET subscriptions/me`'s job, so history is exactly "what used to be
  true but no longer is."
- **`ISubscriptionService.GetUserSubscriptionHistoryAsync(userId, pagingDto)`** composes the same
  reusable specifications the admin listing already uses -
  `UserIdEqualsSpecification<UserSubscription, long>(userId).And(statusSpec.Or(statusSpec))` for
  the two allowed statuses - no new specification class needed. Projects into
  `UserSubscriptionHistoryDto` (`Core/Data/Dto/Subscription/`), a self-service-only shape: no
  `UserId`/`UserEmail` (the caller already knows who they are) and no
  `ExternalSubscriptionId`/`Gateway` (admin-only, same reasoning as `subscriptions/me` itself).
  No `FeatureGroups`/quota fields either - a lapsed subscription's quota buckets aren't
  meaningful to show once it's no longer active.
- No new entity or migration - `UserSubscription.Status` already covers all four states end to
  end; this is purely a new read path over existing data.

## Quota consumption and the points fallback

`SubscriptionQuotaService.ConsumeQuotaAsync(userId, featureCode, amount)`:

1. Selects a candidate quota row: an `Active`, non-expired subscription with
   `Limit IS NULL OR Used + amount <= Limit` for that feature (earliest-expiring
   subscription first, if a user happens to have more than one active plan — draining the
   soonest-to-lapse one first is a deliberate, if untested-in-the-UI, product choice).
   A `NULL` `Limit` (unlimited) always qualifies.
2. Performs the decrement as a **guarded `UPDATE`** re-checking the same `Limit IS NULL OR
   Used + amount <= Limit` condition in the `WHERE` clause and inspecting rows-affected —
   this is what makes concurrent
   consumption safe without locking: two simultaneous requests against the last unit of
   quota can't both succeed, and the loser retries once against a fresh read before
   giving up.
3. On failure, classifies *why* (`NoActiveSubscription` / `FeatureNotInPlan` /
   `QuotaExhausted`) and looks up **upgrade suggestions** — active plans whose limit for
   that feature exceeds the user's current one (an unlimited plan feature always counts as
   an upgrade over a finite limit; if the user's current limit is itself already
   unlimited, nothing is suggested) — so the caller can surface an upsell
   rather than a bare error. Since `BillingInterval` now lives on each candidate plan's
   default `SubscriptionPlanPrice` (a plan can offer several), the candidate query fans
   out across each qualifying plan's default-priced rows, keeps up to the **3 cheapest
   qualifying prices per interval** (cheapest first), then **regroups the survivors by
   plan** — `UpgradeSuggestions` is `IEnumerable<UpgradeSuggestionDto>`, one entry per
   plan (not per interval). Each entry's own plan id is exposed as `Id` (not
   `SubscriptionPlanId`), deliberately matching `ActiveSubscriptionPlanResponseViewModel.Id`
   (`subscriptions/plans`) so a suggestion entry is schema-compatible with a plan card
   wherever a client renders either one - e.g. a shared "subscribe to this plan" component
   used both in a wallet/plans screen and in an insufficient-balance upsell during download
   doesn't need to remap the id field per source (renamed from `SubscriptionPlanId` for this
   reason; the DB column/internal grouping key of the same name, described elsewhere in this
   doc, is unaffected). Each entry also carries a nested `Prices: IEnumerable<UpgradeSuggestionPriceDto>`
   — one row per billing interval that plan qualified at. This avoids repeating
   `Title`/`Highlight`/`FeatureGroups` once per interval the way a period-keyed dictionary
   would: a plan offered at both Monthly and Yearly appears **once**, with two entries in
   its `Prices` list, not twice in the top-level collection. Each `Prices` entry carries
   `BillingInterval`, that interval's default (global) `Price`/`Currency`/`CurrencySymbol`,
   `MonthlyEquivalentPrice` (`Price` normalized to a per-month cost via
   `BillingInterval.Days`, always set), and `DiscountPercent` (savings vs. this *same
   plan's own* Monthly price — resolved via the shared `SubscriptionPlanId`, never a
   title/tier guess; `null` for the Monthly entry itself or when the plan has no Monthly
   price to compare against). Alongside `UpgradeSuggestions`, the response also carries
   `AvailableBillingIntervals: IEnumerable<string>` — the distinct interval names present
   anywhere in the suggestions, in interval order (e.g. `["Monthly", "Yearly"]`) — a
   ready-made tab manifest so a client doesn't have to scan every plan's `Prices` just to
   know which period tabs to render, especially since different plans aren't required to
   offer the same set of intervals. `UpgradeSuggestionDto.PooledFeatureCodes`/`Description`
   carry through the *specific* failed feature's pooling — if `Limit` here is a pooled
   bucket also covering another feature (see `SubscriptionPlanFeature.FeatureGroupKey`
   above), `Description` is already resolved to the pool's description and
   `PooledFeatureCodes` names the sibling(s), so the caller can say "500, shared with Exam
   Downloads" without cross-referencing the nested `FeatureGroups` list itself. That
   `FeatureGroups` list is `IEnumerable<PlanFeatureGroupDto>` — the plan's *entire* feature
   set, grouped the same way (one entry per bucket, `Limit`/`Description` stated once even
   when its own `Features` list inside has several codes) — for the plan-card display of
   the *rest* of the plan's features. Plan data (`Highlight`, `Prices`, `FeatureGroups`) is
   fetched directly against `SubscriptionPlan` inside `SubscriptionQuotaService` — it
   deliberately does **not** call `ISubscriptionService`
   to get this, because `SubscriptionService -> PaymentService -> ISubscriptionQuotaService`
   already exists, and `SubscriptionQuotaService -> ISubscriptionService` would close that
   into a circular dependency the DI container rejects at startup. The client can render
   an upgrade modal straight from this one response, no second `GET /plans` call needed.

**`GameService.SpendPointsAsync`** (the existing `games/spends` endpoint, pastpaper/test
downloads) wires this in ahead of the wallet: it tries `ConsumeQuotaAsync` first
(`FeatureCodes.PastpaperDownload`/`TestDownload`); if consumed, the action succeeds with
**no wallet debit and no `Transaction` row at all** — the subscription is a separate
entitlement track, not a points top-up. If quota isn't available (no subscription,
feature not in the plan, or exhausted), it falls through to the pre-existing
points-balance check/debit unchanged. This means non-subscribers see zero behavior
change.

The response now distinguishes which path paid for the action and carries upgrade
suggestions when relevant:

- `POST api/v1/games/spends` — unchanged wire shape (`ApiResponse<bool>`), for existing
  clients.
- `POST api/v2/games/spends` — richer `ApiResponse<{ spent, paidBy, remainingQuota,
  upgradeSuggestions[] }>`, for clients that want to show the upsell.

`TestSubmission`/`ExamParticipation` are cataloged `Feature` rows but **not yet wired** —
those actions remain free-to-attempt exactly as before (points only flow as a
correctness reward/penalty, per `docs/business/payments-and-points.md`).

## Expiry: forfeiture, not clawback

Unused quota is simply lost at period end — nothing is deducted from a user's points
wallet, since quota was never points to begin with. Expiry is enforced two ways:

- **Lazily**, inside `ConsumeQuotaAsync`'s candidate query (`ExpirationDate > now`) — an
  expired subscription's quota is invisible to consumption immediately, without waiting
  for a batch job.
- **In batch**, via the `ExpireOverdueSubscriptions` Hangfire recurring job
  (`Cron.Daily(0, 40)`, registered in `Startup.cs` alongside the other daily jobs — see
  `docs/database/migrations.md`'s `SchoolRank`/Hangfire notes for the sibling pattern),
  which flips overdue `Active` rows to `Expired` for reporting/listing cleanliness. The
  lazy check means correctness never depends on this job having run recently; it exists
  so "my active subscriptions" listings don't show a lapsed plan as still `Active`.

## Deliberately out of scope for this phase

- **PayPal.** `Payment.Gateway`/`SubscriptionPlanGatewayMapping.Gateway` will need a
  `PayPal` member added when that integration lands — the recurring-billing pipeline
  below is already gateway-parameterized for this (route, factory, interface), so it's
  purely additive: a `PayPalRecurringPaymentGatewayProvider` implementing
  `IRecurringPaymentGatewayProvider`, nothing else changes.
- **Native recurring billing.** ~~The current purchase flow is one-time checkout~~ — built
  2026-08-10 for Stripe, see "Native recurring billing (Stripe)" below. GamaTrain still
  never auto-renews (no saved-payment-method concept for a crypto wallet).
- **A real FX source.** `Payment.BaseCurrencyAmount`/`ExchangeRate` (see
  `docs/business/payments-and-points.md`) use a pragmatic 1:1 peg for USD-stable
  currencies only; `SOL`/`GET` are left `null` pending an actual rate source.
- **In-house pastpaper file serving.** Delivery is proxied to a separate legacy backend;
  quota/charge enforcement happens here, the file itself doesn't. If that proxy is later
  replaced with in-house serving, it should sit behind a provider interface (mirroring
  `IPaymentGatewayProvider`'s `IGenericFactory` pattern) so the swap doesn't touch the
  quota-check code path.
