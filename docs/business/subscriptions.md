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
  Monthly" and "Pro Annual" had to be two disconnected `SubscriptionPlan` rows with no
  relationship, breaking any honest Monthly-vs-Annual comparison in upgrade-suggestion
  UX. Moving it to `SubscriptionPlanPrice` means Monthly and Annual are just sibling
  prices under the same plan — no linking table needed.
- **`BillingInterval` member names renamed to match industry-standard billing terms**
  (2026-08-19): `Seasonally` → `Quarterly` and `Yearly` → `Annual` (`Daily`/`Weekly`/`Monthly`
  unchanged). This is a **pure C# symbol rename** — each member's underlying `Value` (byte)
  and `Days` are untouched (`Quarterly` is still `3`/90 days, `Annual` is still `4`/365 days),
  so every already-persisted `SubscriptionPlanPrice`/`SubscriptionPlanFeature`/
  `UserSubscription` row (stored as `tinyint`, not as a name string) resolves correctly with
  no data migration. It **is** a breaking change to the JSON wire contract though: every
  `BillingInterval` field serializes as the plain `Name` string (see
  `EnumerationConverter<TEnum,TKey>`), so any endpoint that used to return/accept `"Yearly"`
  or `"Seasonally"` now returns/accepts `"Annual"`/`"Quarterly"` instead — requires a
  coordinated frontend/mobile deploy, not just a backend one.
- **`SubscriptionPlanFeature`** — `(SubscriptionPlanId, FeatureId, BillingInterval, Limit,
  FeatureGroupKey, FeatureGroupDescription)`, unique on `(SubscriptionPlanId, FeatureId,
  BillingInterval)`. One row per feature a plan grants **at one billing interval** — a
  plan's Monthly and Annual variants can now grant different limits for the same feature
  (2026-08-13 redesign; previously this was plan-wide/interval-agnostic, meaning buying
  Annual granted the exact same number as Monthly, just for a longer period, which under-
  rewarded longer commitments). `BillingInterval` here is deliberately **not** the same
  axis as price/currency: two regional `SubscriptionPlanPrice` rows for the same plan and
  interval still grant identical quota (see "quota, not currency" above — that rule is
  about `Price`/`Currency`, never about which interval SKU was bought). Admins set a limit
  per interval explicitly, one number at a time — there's no automatic multiplier (e.g. no
  built-in "Annual = 12× Monthly"); a feature added to a plan without a limit defined for
  every interval it's sold at grants **zero** quota for that feature at the interval left
  unset, not the same number as another interval, so keeping every sold interval's limit
  filled in is an admin responsibility, not something the system infers.
  `FeatureGroupKey` (nullable string, added 2026-08-03) is how two or more features on the
  same plan **pool onto one shared quota** instead of each getting an independent one —
  e.g. `ExamDownload` and `PastpaperDownload` both drawing down the same 500-unit bucket.
  Which features are pooled together is interval-invariant (the same key is reused across
  every interval row of the group) — only the `Limit` number varies per interval. It's set
  via `SetPlanFeaturesAsync`'s request shape, `FeatureGroups: [{ FeatureIds: [...], Limits:
  [{ BillingInterval, Limit }, ...], Description }]` — admin/caller expresses pooling by
  putting feature ids in the same array entry, never by inventing/matching a string
  themselves; the key itself is a **server-generated GUID**, purely a DB linking detail
  invisible to the API (a group with one `FeatureId` gets `FeatureGroupKey = NULL`, today's
  unpooled behavior). `Limits` is sparse — only the intervals actually being defined need
  an entry, no requirement to cover every `BillingInterval` value; a duplicate interval
  within one group's `Limits` is rejected (`NotValid` / `DuplicateBillingIntervalInFeatureGroup`).
  `Description` is **required whenever a group pools 2+ features** (`NotValid` /
  `FeatureGroupDescriptionRequired` otherwise) — a pooled bucket has no single feature to
  describe it, so unlike a single-feature entry (which already has `Feature.Description`
  to show), the group needs its own; it's ignored for a single-feature entry.
  `GetPlanFeaturesAsync` surfaces pooling back as `PlanFeatureGroupDto` — one entry per
  bucket, `Features: [{ FeatureId, FeatureCode, FeatureName }, ...]` (one entry when
  unpooled, several when pooled) plus `Limits: [{ BillingInterval, Limit }, ...]` and a
  single `Description` shared by the whole group (`Description` already resolved: the
  pool's when pooled, otherwise the single feature's own `Feature.Description`). This
  mirrors `SetPlanFeaturesAsync`'s write shape exactly so a pooled group is one entry with
  N feature codes and its per-interval limits inside it, never N entries repeating the same
  limits and description (2026-08-04 fix — it originally stayed flat, one row per feature,
  with a `PooledFeatureCodes` list bolted on to point at siblings, which just meant the
  group's `Limit`/`Description` were duplicated once per feature in the group instead of
  stated once).
- **`SubscriptionPlanPrice`** — `(SubscriptionPlanId, CountryCode, Currency, Price,
  BillingInterval)`. `CountryCode = NULL` is the **global default** price for that
  interval, and a unique index on `(SubscriptionPlanId, CountryCode, BillingInterval)`
  guarantees at most one row per plan+country+interval combination (SQL Server treats
  `NULL` as a distinct value in unique indexes, so this doesn't collide across rows). A
  single plan can now have several price rows — one per `BillingInterval`
  (Daily/Weekly/Monthly/Quarterly/Annual) it's offered at, each with its own default
  (and, once regional pricing is enabled, per-country) price — regional pricing is built
  but dormant, see below. `GET admin/subscriptions/prices` accepts an optional
  `subscriptionPlanId` filter (2026-08-13, via the previously-unused
  `PlanIdEqualsSpecification` over `SubscriptionPlanPrice`) to list one plan's price rows without
  fetching the whole plan object — `GET admin/subscriptions/plans/{id}` already returns the
  same rows nested under `prices`, this is just a lighter-weight alternative.
- **`SubscriptionPlanGatewayMapping`** — `(SubscriptionPlanPriceId, Gateway,
  ExternalProductId, ExternalPlanId)`. Keyed off the *price* row, not the plan, because
  gateway Product/Price objects (Stripe Prices, PayPal Plans) are currency- **and**
  interval-bound — a Turkey-TRY-Monthly price, a US-USD-Monthly price, and a US-USD-Annual
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
  activation time, matched to the subscription's own `BillingInterval` — a Monthly
  subscriber and an Annual subscriber of the same plan can snapshot different numbers) and
  `Used`. `Description` is also snapshotted at activation, already
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
  concurrency-safety story at all. Since 2026-08-13, each bucket in `GET subscriptions/me`
  also carries `planLimits: [{ billingInterval, limit }]` — the current plan's own limit
  at *every* interval it's sold at, fetched live (not snapshotted) alongside the
  subscriber's own `limit`/`used`/`remaining`. This lets a client show "you're on Monthly:
  50, this plan's Annual: 600" directly on the subscription screen, without waiting for a
  quota-exhausted upgrade suggestion. Matched by `FeatureId` against the bucket's own
  `Features`, not by replaying the (possibly stale) `FeatureGroupKey` the bucket was
  snapshotted with at activation — so it reflects the plan's *current* configuration, which
  can drift from what was true when the subscriber activated (e.g. an admin later changes
  the plan's per-interval limits).

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
   client-supplied price, only which interval to resolve.

   **If the caller already has an `Active` subscription, this call is delegated to
   `SwitchSubscriptionPlanAsync` instead of inserting a second one** (fixed 2026-08-15,
   found live: a user with simultaneously-Active Alpha + Beta subscriptions, both real,
   independently-renewing Stripe subscriptions, each charging the card on its own
   schedule — see "Quota consumption and the points fallback" below for why the backend
   never previously blocked this). The first cut of this fix (2026-08-15) simply
   rejected the second purchase with `OperationResult.Duplicate` and required the client
   to call `SwitchSubscriptionPlanAsync` itself; as of 2026-08-16 `purchase` detects the
   existing subscription and performs the switch in place, so **a single "buy this plan"
   button works for both a fresh purchase and a change to an existing one** — the client
   no longer has to check subscription state and branch between the two endpoints (see
   "Purchase now also performs switches" below for the exact response shape). This
   delegation is by request body only, not `Confirm` semantics: an existing
   non-recurring subscription still gets `SubscriptionNotRecurring`, an identical
   plan+interval still gets `SamePlanSwitchNotAllowed`, and a smaller-interval request
   still gets `IntervalDowngradeNotSupported` — all the same rejections
   `SwitchSubscriptionPlanAsync` itself returns, just reached through `purchase`.
   `SwitchSubscriptionPlanAsync` (below) remains available directly and is the better
   fit for a dedicated "manage my subscription" screen; `purchase` is now the
   recommended single entry point for a generic buy/upgrade UI.

   For a genuinely new subscription (no existing `Active` row), the endpoint validates
   the plan is active, resolves its price via `ResolvePriceAsync` (filtered on plan +
   country + `BillingInterval`), inserts a `UserSubscription` row (`Status = Pending`,
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

### Immediate plan-switch charges weren't recorded as Payments (fixed 2026-08-16)

Found live in the sandbox admin `payments` report: buy a plan, then upgrade it - the initial
purchase shows up, the upgrade's proration charge doesn't, even though Stripe genuinely charged
the card (see "Plan upgrade/downgrade with proration" above - an immediate upgrade bills
synchronously via `ProrationBehavior = "always_invoice"`).

Root cause: `ParseWebhookEventAsync`'s `invoice.paid` match was restricted to `BillingReason ==
"subscription_cycle"` (an ordinary renewal) to avoid double-recording the *first* invoice
(`subscription_create`, already handled by the client-driven verify flow - see "Renewal is
webhook-driven" above). That comment only accounted for two billing reasons. Stripe uses a
*third* one for this exact case: `subscription_update` - the prorated invoice an immediate plan/
interval switch generates. Unmatched by either branch, it fell to the `Ignored` default, so its
`invoice.paid` webhook was silently dropped - a real charge, invisible in `admin/payments`.

- **`RecurringWebhookEventType` gains a fourth member, `PlanChangeInvoicePaid`**, matched
  separately from `InvoicePaid` when `BillingReason == "subscription_update"`. Deliberately its
  own event type, not folded into `InvoicePaid`, because the two need different handling below,
  not just a different `Payment.Amount` source.
- **Records the `Payment` row using the invoice's own `AmountPaid`**, never `UserSubscription.
  PricePaid` the way `HandleInvoicePaidAsync` does for an ordinary renewal. By the time this
  webhook arrives, `PricePaid` has already been overwritten to the *new* plan's full price by
  `ApplyPlanSwitchAsync` (called synchronously right after the Stripe update call, well before
  the async webhook) - using it here would have recorded the full new price (e.g. $10) instead
  of the actual prorated charge (e.g. $4). `RecurringWebhookEventDto` gained a matching nullable
  `Amount` (decimal, already divided from Stripe's cents), populated only for this event type.
- **Deliberately never calls `RenewSubscriptionAsync`** - the bug this guards against, not just
  an oversight: a plan-change invoice doesn't represent a new billing period, so extending
  `ExpirationDate` and resetting quota `Used` back to `0` (`RenewSubscriptionAsync`'s job for an
  actual renewal) would incorrectly give the caller a free extra period and a quota refresh as a
  side effect of upgrading mid-cycle.
- **Same idempotency guard as `HandleInvoicePaidAsync`** - `Payment`'s existing
  `(TransactionId, Gateway)` unique index catches a redelivered event
  (`UniqueConstraintException`, swallowed) - simpler here than the renewal case, since there's no
  second step (a renewal call) that also needs skipping on redelivery.
- **Verified live** against a local SQL Server + running API, using a Stripe.net-signed
  synthetic `invoice.paid` event (`billing_reason: "subscription_update"`, no real Stripe account
  involved): a `Payment` row was correctly recorded with the invoice's own $4 amount;
  `ExpirationDate` and quota `Used` were both confirmed unchanged; redelivering the identical
  event produced no second row.

### Dunning visibility (`invoice.payment_failed`)

Built 2026-08-14, found while auditing which subscription lifecycle actions actually depend on a
webhook (cancel and downgrade both correctly rely on the two events above; this was the one genuine
gap — `invoice.payment_failed` wasn't recognized at all, not even as an enum member, so a failed
renewal charge was completely invisible locally for the entire length of Stripe's retry window,
which can run for weeks).

This is **visibility only, deliberately not an access-control change** — it does not touch the
"Dunning is entirely Stripe's" decision above, which still stands: no local retry/grace-period logic
was added, Smart Retries are still entirely Stripe's job.

- `RecurringWebhookEventType` gained a third member, `PaymentFailed`, alongside the existing
  `InvoicePaid`/`SubscriptionEnded`. `StripePaymentGatewayProvider.ParseWebhookEventAsync` now also
  matches `invoice.payment_failed`, resolving `userSubscriptionId` from
  `Invoice.Parent.SubscriptionDetails.Metadata` the same way `invoice.paid` does — but **not**
  restricted to `BillingReason == "subscription_cycle"` the way `InvoicePaid` is, since there's no
  double-recording risk to guard against here (unlike a successful charge, a failed one never writes
  a `Payment` row or touches `ExpirationDate`), so a failed *first*-period charge
  (`subscription_create`) is surfaced too.
- `UserSubscription.LastPaymentFailedDate` (new nullable column, migration
  `AddLastPaymentFailedDateToUserSubscription`) is stamped by a new
  `PaymentService.HandlePaymentFailedAsync` (guarded on `Active`, same idempotent-no-op style as
  `CancelSubscriptionAsync`) and cleared back to `null` inside `RenewSubscriptionAsync`'s two success
  paths (plain renewal and pending-downgrade-applies) — the next successful charge, whenever it
  comes, clears it.
- Exposed on both `GET subscriptions/me` (`UserSubscriptionResponseViewModel.lastPaymentFailedDate`)
  and the admin `GET admin/subscriptions/users`/`users/{id}` (`AdminUserSubscriptionResponseViewModel`,
  same field) — a client can now show a "payment failed, please update your card" prompt during
  Stripe's own dunning window, and support can see it on the admin side too. `Status`,
  `ExpirationDate`, and quota consumption are all completely unaffected by this field — a subscriber
  with a non-null `LastPaymentFailedDate` is exactly as usable as one without, right up until
  `ExpirationDate` (unchanged "forfeiture, not clawback" behavior) or an eventual
  `customer.subscription.deleted` once Stripe's retries exhaust.
- **Verified live** against a local SQL Server and the real running API: a Stripe.net-signed synthetic
  `invoice.payment_failed` event (built from real `Stripe.Event`/`Invoice` objects, signed with
  `EventUtility.ComputeSignature` using a local-only secret - no real Stripe account involved) stamped
  `LastPaymentFailedDate` with `Status`/`ExpirationDate` both unchanged, and a subsequent successful
  `invoice.paid` cleared it back to `null` while extending `ExpirationDate` normally.

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
  Stripe's own recurring billing next charges a gateway-recurring subscription. Also never resets quota
  `Used` the way a real renewal (`RenewSubscriptionAsync`) does - if the caller needs both the period
  extended *and* quota refreshed, `extend` alone isn't equivalent to a real renewal.
- **`POST users/{id}/resync`** (added 2026-09-05, see "Follow-up: reconciling against the
  gateway before expiring" below) — the source-of-truth counterpart to `extend`: reads the
  gateway's own live status/current period end and, if it confirms the subscription is still
  active, syncs `ExpirationDate` directly to it and resets quota, instead of a guessed day
  count. `NotValid`/`SubscriptionNotRecurring` for a one-time/GamaTrain subscription.
- **`GET users/{id}` gains `featureGroups`** (added 2026-08-17, found live: a support case needed to know
  whether a specific customer could still use their remaining subscription after an admin `revoke` on a
  duplicate, and there was no way to see that anywhere - the detail view had every subscription field
  *except* its actual quota state). One entry per quota bucket, each carrying the *live* `Limit`/`Used`/
  `Remaining` (`Remaining = Limit - Used`, floored at 0; `null` when `Limit` is `null`/unlimited) alongside
  the same `Features`/`Description` shape `UpgradeSuggestionFeatureGroupDto` already uses elsewhere - unlike
  that DTO, which describes what a plan *offers*, this describes what's actually been consumed against
  *this specific* subscription right now. Deliberately scoped to the single-subscription detail call only,
  not the paged `GET users` list - it needs its own query per subscription (`UserSubscriptionQuota` +
  `UserSubscriptionQuotaFeature` + `Feature`, joined and grouped), which would be wasteful to run for every
  row of a paginated list. Before this, there was genuinely no way to answer "can this user still download
  right now" from any admin endpoint - the closest existing tool, `GET users/usage/aggregate`, gives
  consumption totals over a date range but never the plan's own `Limit` to compare against. Verified live
  against a local SQL Server + running API: a subscription with a capped bucket (`Limit: 300, Used: 45`)
  correctly returned `Remaining: 255`; an unlimited bucket (`Limit: null, Used: 5`) correctly returned
  `Remaining: null`, not a divide-by-null error; the paged list confirmed `featureGroups` stays unset there.

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
- **Guarded against double-charging a genuine duplicate/concurrent request** (fixed 2026-08-16, found while
  reasoning through a real upgrade scenario). An upgrade bills the card *immediately*
  (`ProrationBehavior = "always_invoice"`, below) - but the gateway call happened before any local write, and
  `StripePaymentGatewayProvider`'s `RequestOptions` property mints a fresh `IdempotencyKey =
  Guid.NewGuid().ToString("N")` on every access, so Stripe had zero way to recognize a retried/duplicated call
  as the same operation. A double-click, browser double-submit, or client retry after a perceived timeout could
  reach Stripe twice and generate two separate proration invoices for one logical click. Fixed with a new
  nullable `UserSubscription.SwitchLockedUntil` column, claimed via a **guarded conditional `UPDATE`** (`WHERE
  Status == Active AND (SwitchLockedUntil IS NULL OR SwitchLockedUntil < now)`, same concurrency-safety pattern
  quota consumption already uses) taken **before** the gateway call, not after - a concurrent second request
  sees the claim still in the future and is rejected locally (`SwitchAlreadyInProgress`, `OperationResult.
  Duplicate`) without ever reaching Stripe. 30-second TTL, not tied to completion, so a failed attempt isn't
  blocked forever; `ApplyPlanSwitchAsync`/`RequestPlanSwitchAsync` clear it immediately once a switch actually
  completes rather than waiting out the TTL. Verified live: a claim taken while the lock is already held is
  rejected with zero gateway calls made; the same guarded-update query claims successfully once the lock has
  expired, and correctly loses to a second claim attempt immediately after.
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

### Switching billing interval (not just plan), for a bigger interval only

Added 2026-08-16. Until this, `switch` only ever changed `SubscriptionPlanId` — `ResolvePriceAsync` was
hardcoded to always resolve at the subscription's own current `BillingInterval`, and the same-plan guard
only compared plan id, so "same plan, different interval" (e.g. Alpha Monthly → Alpha Annual) had no
supported path at all: `switch` rejected it as `SamePlanSwitchNotAllowed`, and (after the
duplicate-active-subscriptions fix above) `purchase` correctly rejects it too, since the user already
has an Active subscription. A user wanting a longer interval had no route except cancel → wait for it to
actually lapse → purchase fresh.

This was worth fixing specifically because of the per-interval quota work from 2026-08-13: Monthly and
Annual of the same plan can now carry genuinely different limits, not just different prices, so moving to
a bigger interval can be a real quota upgrade, not merely a payment-cadence preference — the same
category of thing `switch` already exists to handle for plan tiers.

- **`POST subscriptions/me/switch` gains an optional `billingInterval`** (`SwitchSubscriptionPlanRequestDto`/
  ViewModel) — omitted keeps today's exact behavior (plan-only switch, interval untouched). When set, this
  is either a plan+interval switch in one call, or (`subscriptionPlanId` unchanged) a bare interval move on
  the same plan.
- **The same-plan guard now compares plan *and* interval together** — `SamePlanSwitchNotAllowed` only
  fires when neither would change; "same plan, different interval" now passes through to price resolution
  instead of being rejected outright.
- **Price is resolved at the requested interval**, not the subscription's current one - `ResolvePriceAsync`
  already supported this, it was just being force-fed the wrong interval before.
- **Reuses the existing immediate/deferred price-comparison rule unchanged, deliberately** - no separate
  "is this an interval upgrade" concept was added. A bigger interval's total price is always numerically
  greater than a smaller one's for the same plan, so the existing `immediate = targetPrice > currentPricePaid`
  rule already classifies a move to a bigger interval as immediate, with no new decision logic to keep in
  sync with the plan-upgrade rule.
- **`ApplyPlanSwitchAsync` (the immediate-switch path) now also sets `BillingInterval`** on the
  `UserSubscription` row and re-snapshots quota at the *new* interval, not the old one - previously this
  method didn't need an interval parameter at all, since a switch could never change it.
- **A move to a smaller interval was originally rejected outright** (`IntervalDowngradeNotSupported`) for
  two stated reasons: the deferred/schedule path had no `PendingSwitchBillingInterval` to carry an interval
  change through to `RenewSubscriptionAsync`, and unused already-paid-for time on a longer interval (e.g.
  Annual → Monthly mid-year) seemed to raise a refund/credit policy question. **Fixed 2026-08-19** (reported
  live: the endpoint is supposed to allow downgrades, and rejecting outright was wrong even for a plain
  plan downgrade that happened to also request a smaller interval) - see "Interval downgrade now supported,
  deferred to period end" below; this closed both original reasons without needing a refund/credit policy
  after all.
- **Verified live**, not just compiled, against a local SQL Server + the real running API without ever
  calling real Stripe: same-plan+same-interval still correctly rejected (`SamePlanSwitchNotAllowed`,
  regression check); same-plan+bigger-interval correctly passes every guard (plan/interval check, price
  resolution at the new interval, currency match, gateway mapping lookup, `immediate = true`
  classification) all the way through to the `SwitchLockedUntil` claim, confirmed by pre-holding that lock
  and observing the expected `SwitchAlreadyInProgress` rejection rather than an earlier, unrelated failure.

### Interval downgrade now supported, deferred to period end (fixed 2026-08-19)

Live-reported bug: `POST subscriptions/me/switch` rejected every interval downgrade (and a plan-tier
downgrade that also happened to request a smaller interval) with `IntervalDowngradeNotSupported` - the
endpoint is supposed to allow downgrades, not block them. The original rejection reasons (above) both
dissolved once this was actually implemented, because it reuses the exact deferral a plan-only downgrade
already gets ("keep what you have until period end") rather than doing anything immediately - nothing is
billed differently in the meantime, so there was never a real refund/credit question to answer for this
path specifically.

- **New nullable `UserSubscription.PendingSwitchBillingInterval` column** (migration
  `AddPendingSwitchBillingIntervalToUserSubscription`), paired with the existing
  `PendingSwitchSubscriptionPlanId`/`PendingSwitchPricePaid` - set together by `RequestPlanSwitchAsync`
  (now takes the caller's resolved target interval as a required parameter, mirroring
  `ApplyPlanSwitchAsync`'s existing `newBillingInterval` parameter), cleared together by
  `RenewSubscriptionAsync` once applied and by `RequestCancellationAsync` (a pending switch doesn't matter
  once cancellation is requested, same as the other two pending-switch fields).
- **`SwitchSubscriptionPlanAsync` no longer special-cases `!immediate && targetInterval !=
  subscription.BillingInterval`** - that combination now flows into the same deferred branch a plan-only
  downgrade already used, just also carrying `targetInterval` into `RequestPlanSwitchAsync`.
- **No gateway-side change was needed.** The Stripe deferred-switch mechanism
  (`StripePaymentGatewayProvider.SwitchSubscriptionPlanAsync`, `immediate: false`) already builds a
  2-phase Subscription Schedule keyed only by the new *Price* id - it was already interval-agnostic, since
  a Price's `recurring.interval` isn't inspected by that code path at all. The only work was carrying the
  target interval through the local pending-switch bookkeeping.
- **`RenewSubscriptionAsync` computes the post-switch `ExpirationDate` using the *new* interval**, not the
  interval that just ended - the period Stripe's schedule starts at this same boundary runs on the new
  interval's own length (e.g. an Annual → Monthly downgrade's next period is one month, not one year, from
  the old `ExpirationDate`). Falls back to the subscription's pre-switch interval when
  `PendingSwitchBillingInterval` is `null`, which only happens for a plan-only pending switch recorded
  before this column existed - exactly the right fallback either way.
- **Exposed on `GET subscriptions/me`/`GET admin subscriptions/users(/{id})`** as
  `pendingSwitchBillingInterval`, alongside the existing `pendingSwitchPlanId`/`pendingSwitchPlanTitle` -
  `null` whenever those are, otherwise the interval the pending switch takes effect at (equal to the
  subscription's current `billingInterval` when the pending switch doesn't also change interval).

### Purchase now also performs switches, with a confirm step for real charges

Added 2026-08-16, on top of the delegation described in "Purchase → verify → activate
lifecycle" above and the interval-switch work directly above this section. Two problems this
closes: (1) a client had to know whether the caller was already subscribed and call a
different endpoint accordingly, and (2) once `purchase` could trigger an immediate,
synchronous Stripe charge (an upgrade), it needed the same "show the amount before charging
it" safety a payment action normally gets, which a plain purchase-and-redirect-to-Checkout
flow never needed before (Checkout itself shows the amount; a direct proration charge does not).

- **`PurchaseSubscriptionRequestDto`/`SwitchSubscriptionPlanRequestDto` both gain a `Confirm`
  flag** (`bool`, defaults `false`). It only has an effect on the one path that bills
  synchronously: an immediate upgrade. A downgrade, a fresh purchase, and every rejection
  path ignore it entirely.
- **An immediate upgrade with `Confirm = false` no longer applies anything.** It calls the new
  `IRecurringPaymentGatewayProvider.PreviewSwitchSubscriptionPlanAsync` — Stripe's own
  `InvoiceService.CreatePreviewAsync` with the item's price swapped to the target and
  `ProrationBehavior = "always_invoice"`, the same proration Stripe would actually apply, just
  requested as a preview rather than a real invoice — and returns `RequiresConfirmation = true`
  plus `PreviewAmount`/`PreviewCurrency`, without touching Stripe's subscription, without
  claiming the `SwitchLockedUntil` lock, and without any local write. This is a pure read;
  calling it repeatedly is safe and does not need the double-charge guard described above,
  because nothing is charged or locked until a `Confirm = true` resubmit.
- **The same request resubmitted with `Confirm = true`** proceeds exactly as an immediate
  switch already did (`SwitchLockedUntil` claim → Stripe `UpdateAsync` with
  `ProrationBehavior = "always_invoice"` → local write), described above. There is no
  server-side memory of the previewed amount between the two calls — the second call
  re-resolves price and re-previews internally via the same code path, so if the plan's price
  changed between preview and confirm, the confirm call charges the *current* price, not
  whatever was shown in the preview. Given the ~30-second `SwitchLockedUntil` TTL and how
  rarely admin price edits happen mid-checkout, this was accepted rather than adding a
  short-lived server-side preview token.
- **`purchase` inherits this transparently** — `PurchaseSubscriptionAsync`'s delegation to
  `SwitchSubscriptionPlanAsync` passes `Confirm` straight through, and the response DTO
  carries `Switched`/`RequiresConfirmation`/`PreviewAmount`/`PreviewCurrency` alongside the
  existing `Url`/`PaymentId` fields — `Url` stays `null` whenever `Switched` or
  `RequiresConfirmation` is set, since neither has a Checkout session to redirect to.
- **`POST subscriptions/me/switch` is unchanged in shape and still exists** — this was a
  deliberate choice, not an oversight: it stays the better fit for a dedicated
  "manage my subscription" screen that already knows the caller is subscribed, while
  `purchase` becomes the one endpoint a generic buy/upgrade button needs regardless of
  subscription state. See the frontend-facing integration note shared alongside this change
  for the exact request/response payloads per scenario.

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
   `QuotaExhausted`) and looks up **upgrade suggestions** so the caller can surface an
   upsell rather than a bare error. Since `BillingInterval` now lives on each candidate
   plan's default `SubscriptionPlanPrice` (a plan can offer several), the candidate query
   fans out across each active plan's default-priced rows that offer the failed feature at
   all, then **regroups by plan** — `UpgradeSuggestions` is `IEnumerable<UpgradeSuggestionDto>`,
   one entry per plan (not per interval). Each entry's own plan id is exposed as `Id` (not
   `SubscriptionPlanId`), deliberately matching `ActiveSubscriptionPlanResponseViewModel.Id`
   (`subscriptions/plans`) so a suggestion entry is schema-compatible with a plan card
   wherever a client renders either one - e.g. a shared "subscribe to this plan" component
   used both in a wallet/plans screen and in an insufficient-balance upsell during download
   doesn't need to remap the id field per source (renamed from `SubscriptionPlanId` for this
   reason; the DB column/internal grouping key of the same name, described elsewhere in this
   doc, is unaffected). Each entry also carries a nested `Prices: IEnumerable<UpgradeSuggestionPriceDto>`
   — one row per billing interval that plan qualified at. This avoids repeating `Title`/
   `Highlight` once per interval the way a period-keyed dictionary would: a plan offered at
   both Monthly and Annual appears **once** at the top level, with two entries in its
   `Prices` list, not twice in the top-level collection. Each `Prices` entry carries
   `BillingInterval`, that interval's default (global) `Price`/`Currency`/`CurrencySymbol`,
   `MonthlyEquivalentPrice` (`Price` normalized to a per-month cost via
   `BillingInterval.Days`, always set), `DiscountPercent` (savings vs. this *same plan's
   own* Monthly price — resolved via the shared `SubscriptionPlanId`, never a title/tier
   guess; `null` for the Monthly entry itself or when the plan has no Monthly price to
   compare against) — **and, since 2026-08-13, that interval's own quota picture**: `Limit`,
   `PooledFeatureCodes`, `Description`, and `FeatureGroups`. These four moved down from the
   top-level `UpgradeSuggestionDto` onto each `Prices` entry because
   `SubscriptionPlanFeature.Limit` is no longer plan-wide — Monthly and Annual of the same
   plan can legitimately grant different numbers now, so "what you'd get" has to be resolved
   per interval, not once per plan. Alongside `UpgradeSuggestions`, the response also
   carries `AvailableBillingIntervals: IEnumerable<string>` — the distinct interval names
   present anywhere in the suggestions, in interval order (e.g. `["Monthly", "Annual"]`) — a
   ready-made tab manifest so a client doesn't have to scan every plan's `Prices` just to
   know which period tabs to render, especially since different plans aren't required to
   offer the same set of intervals. Each `Prices` entry's `PooledFeatureCodes`/`Description`
   carry through the *specific* failed feature's pooling **at that interval** — if `Limit`
   there is a pooled bucket also covering another feature (see
   `SubscriptionPlanFeature.FeatureGroupKey` above), `Description` is already resolved to
   the pool's description and `PooledFeatureCodes` names the sibling(s), so the caller can
   say "500, shared with Exam Downloads" without cross-referencing the nested
   `FeatureGroups` list itself. That `FeatureGroups` list is
   `IEnumerable<UpgradeSuggestionFeatureGroupDto>` — the plan's *entire* feature set **at
   this one interval**, grouped the same way (one entry per bucket, a single `Limit`/
   `Description` stated once even when its own `Features` list inside has several codes;
   unlike the admin-facing `PlanFeatureGroupDto`, which carries every interval's limit at
   once, this one is already resolved to the interval its containing `Prices` entry is
   for) — for the plan-card display of the *rest* of the plan's features at that interval.
   Plan data (`Highlight`, `Prices`, `FeatureGroups`) is fetched directly against
   `SubscriptionPlan` inside `SubscriptionQuotaService` — it deliberately does **not** call
   `ISubscriptionService`
   to get this, because `SubscriptionService -> PaymentService -> ISubscriptionQuotaService`
   already exists, and `SubscriptionQuotaService -> ISubscriptionService` would close that
   into a circular dependency the DI container rejects at startup. The client can render
   an upgrade modal straight from this one response, no second `GET /plans` call needed.
4. **Also carries `CurrentSubscriptionId`/`CurrentPlanId`/`CurrentPlanTitle`** (added
   2026-08-15, threaded through `SpendPointsResponseDto`/`DownloadContentResponseDto` and
   their ViewModels too — `GameService.SpendPointsAsync`, `ContentDeliveryService`'s two
   download paths, `POST v2/games/spends`, `POST downloads`). The caller's own existing
   `Active` subscription (earliest-expiring, if they have more than one - same tie-break as
   the candidate query above), or all three `null` when `Reason == NoActiveSubscription`.
   Exists specifically to close the gap that let a user end up with two simultaneously
   Active, independently-billed subscriptions (found live 2026-08-15): the previous
   response gave a client acting on `UpgradeSuggestions` no way to tell "I already have a
   subscription, so clicking this suggestion should call `SwitchSubscriptionPlanAsync`" from
   "I have nothing, so this should be a fresh `PurchaseSubscriptionAsync` call" - a real risk
   given the suggestion card is *deliberately* schema-compatible with the general
   "subscribe to this plan" card (previous paragraph), inviting exactly this kind of
   shared-component reuse. `PurchaseSubscriptionAsync` also now rejects outright
   (`OperationResult.Duplicate`) if the caller already has an Active subscription, as a
   server-side backstop independent of whether the client makes the right call - see
   "Purchase → verify → activate lifecycle" above.
5. **Every `Prices` entry carries `IsCurrent`/`CanUpgrade` (added 2026-08-16), and the list
   is no longer filtered or capped.** Before this, a (plan, interval) pair only appeared at
   all if its `Limit` genuinely beat the caller's current one - up to the 3 cheapest
   qualifying prices per interval, cheapest first; the caller's own current plan+interval
   never appeared, and neither did a plan/interval offering *equal or less* quota than what
   the caller already has. That meant a client wanting to render a fixed, complete plan grid
   (all plans × all intervals) with non-upgradeable options simply greyed out had no way to
   do it from this response alone - it would have to separately fetch the full catalog
   (`GET subscriptions/plans`) and reconstruct which cards to disable itself, duplicating
   the exact quota-comparison logic this endpoint already does. Now the query returns every
   (plan, interval) pair that offers the failed feature on an active plan, unconditionally,
   each one flagged:
   - **`IsCurrent`**: `true` only for the exact plan+interval the caller is already on. At
     most one `true` across the whole response, and only when the caller has an active
     subscription. Compared directly against the caller's own `UserSubscription.SubscriptionPlanId`/
     `BillingInterval`, not by limit value - an admin raising a plan's live
     `SubscriptionPlanFeature.Limit` after the caller activated doesn't make "switching" to
     the identical subscription selectable.
   - **`CanUpgrade`**: `true` only when this entry is a genuine improvement - `false` for
     `IsCurrent`, and `false` for any plan/interval whose `Limit` doesn't exceed the
     caller's current one (the exact rule that used to gate inclusion entirely: `NULL`
     `Limit` always beats a finite one; if the caller's current limit is itself already
     unlimited, nothing can `CanUpgrade`).

   Scoped deliberately to just this quota-exhausted response, not the general
   `GET subscriptions/plans` catalog - that endpoint has no "the caller just hit a wall on
   this one feature" context to compare against, and no client need for it was identified
   outside this one screen. Verified live against a real local SQL Server + running API: a
   test subscription active on Alpha/Monthly with `PastpaperDownload` exhausted returned
   every plan offering that feature (Pro, Elite, Gama, Beta, Alpha, GamaTest) at every
   interval each is actually sold at (no interval invented for a plan that doesn't sell it) -
   Alpha/Monthly itself came back `isCurrent:true, canUpgrade:false`; Alpha's other
   intervals and Pro (both `Limit = 100`, equal to the caller's current limit) came back
   `isCurrent:false, canUpgrade:false`; GamaTest (`Limit = 5`, lower) likewise
   `canUpgrade:false`; Elite/Gama/Beta (all `Limit > 100`) came back `canUpgrade:true`
   at every interval they're sold at.

**`GameService.SpendPointsAsync`** (the existing `games/spends` endpoint, pastpaper/test
downloads) wires this in ahead of the wallet: it tries `ConsumeQuotaAsync` first
(`FeatureCodes.PastpaperDownload`/`TestDownload`); if consumed, the action succeeds with
**no wallet debit and no `Transaction` row at all** — the subscription is a separate
entitlement track, not a points top-up. If quota isn't available (no subscription,
feature not in the plan, or exhausted), it falls through to the pre-existing
points-balance check/debit unchanged. This means non-subscribers see zero behavior
change.

**The `leader-board` endpoint ranks by `ApplicationUser.CurrentBalance` - a net, spendable
balance, not a lifetime-earned count** (confirmed deliberate, 2026-09-02). Spending points on a
download lowers a user's rank; this is intentional, not a bug to fix, for as long as the legacy
one-time-payment point-purchase model is still live - those points were bought outright, so
spending them down is a real, correct decrease, the same way spending money lowers a bank
balance. **Do not** "fix" this by switching the leaderboard to a lifetime-earned figure, and
**do not** disable the wallet-points fallback above, until existing legacy point balances have
been given time to drain naturally - both are planned future work once that transition is far
enough along, not scheduled yet.

`ConsumeQuotaAsync`'s `amount` is **not always 1** (fixed 2026-08-14). `SpendPointsRequestDto`
carries it as a separate `QuotaAmount` field (default `1`) from `Points` (the wallet-fallback
amount). `ContentDeliveryService` (downloads, see `docs/business/content-delivery.md`,
"Charge: quota-then-points") sets `QuotaAmount` to gama-api's own reported price for the item, so a
500-point file draws 500 units off the subscriber's monthly download allowance instead of the same
flat 1 a 1-point file would. The plain `games/spends` endpoint (`SpendPointsRequestViewModel`) has no
`QuotaAmount` field and so keeps the original flat-1-per-call behavior — its `Points` is
client-supplied and never verified against gama-api, so letting it also drive quota consumption
would let a caller drain a feature's whole allowance in one request. This is a distinct axis from
"quota is never derived from payment amount" (see `CLAUDE.md`): that rule governs a plan's
`SubscriptionPlanFeature.Limit` never depending on which `SubscriptionPlanPrice` was paid for the
*subscription*; this is how much of that fixed limit one action draws down, scaled by the *content
item's* own price.

The response now distinguishes which path paid for the action and carries upgrade
suggestions when relevant:

- `POST api/v1/games/spends` — unchanged wire shape (`ApiResponse<bool>`), for existing
  clients.
- `POST api/v2/games/spends` — richer `ApiResponse<{ spent, paidBy, remainingQuota,
  upgradeSuggestions[] }>`, for clients that want to show the upsell.

`TestSubmission`/`ExamParticipation` are cataloged `Feature` rows but **not yet wired** —
those actions remain free-to-attempt exactly as before (points only flow as a
correctness reward/penalty, per `docs/business/payments-and-points.md`).

## Consumption history & admin usage reporting

Built 2026-08-13 (Trello: "Subscription consumption/usage reporting", found during the subscription
feature-gaps research pass). Before this, `UserSubscriptionQuota.Used` was purely a running counter —
`ConsumeQuotaAsync` did an atomic guarded increment with no record of individual events, so there was
no way to answer "when did this user consume this feature" or "how much of feature X got consumed
last week across everyone."

- **`SubscriptionQuotaConsumptionLog`** — one row per successful consumption, written by
  `ConsumeQuotaAsync` immediately after its guarded `UserSubscriptionQuota.Used` decrement succeeds
  (`Id`, `UserId`, `UserSubscriptionId`, `FeatureId`, `Amount`, `IdentifierId`, `CreationDate`).
  Deliberately **no FK to `UserSubscriptionQuota`**: `CreateQuotasAsync` deletes and re-snapshots a
  subscription's quota bucket rows on every plan switch or re-activation (see "Plan
  upgrade/downgrade with proration" above), so a hard FK there would either cascade-delete history
  out from under a routine plan switch or block the delete outright. `UserId`/`UserSubscriptionId`/
  `FeatureId` are enough to answer every reporting question this log exists for, and none of those
  rows are ever deleted by a resnapshot. `FeatureId` (not just the bucket) is recorded explicitly so
  a pooled bucket's events still attribute correctly per feature, even though `Used` itself is only
  ever a pooled total.
- **`IdentifierId`** (nullable `long`, implements the same `IIdentifierId` interface `Transaction`
  does, no FK - the owning table depends on the feature, same as `Transaction.IdentifierId`) —
  *which* content item was consumed (e.g. a specific pastpaper/test/exam id), not just that some
  unit of some feature was. `GameService.SpendPointsAsync` already carried `IdentifierId` on its own
  request DTO (used to populate `Transaction.IdentifierId` on the wallet-points fallback path) but
  wasn't passing it into `ConsumeQuotaAsync` - the quota-paid path recorded strictly less detail than
  the points-paid path for the exact same action. Fixed by threading it through
  `ConsumeQuotaRequestDto` → the log insert → `SubscriptionUsageEventDto`/
  `SubscriptionUsageEventResponseViewModel`, and adding it as a `GET admin/subscriptions/usage`
  filter (`IdentifierIdEqualsSpecification<SubscriptionQuotaConsumptionLog>`, the same generic
  specification `GET admin/transactions` already uses for `Transaction`). Both charge paths now
  carry identical detail regardless of which one paid.
- **Deliberately not merged into the `Transaction` ledger.** `GET admin/transactions` is superficially
  similar (paged, filterable by `userId`/`identifierId`/date range), which raises the question of
  reusing that one table/endpoint instead of adding a new one. Rejected: `Transaction` carries
  `Points`/`CurrentBalance`/`IsDebit`/a `PreviousTransactionId` chain - real wallet-balance-movement
  bookkeeping. A subscription-quota consumption never touches the wallet at all (see "The core idea:
  quota, not currency" above), and writing $0 no-op rows into that chain for what's likely the more
  frequent of the two payment paths would pollute a balance-integrity-sensitive ledger with
  non-financial noise, purely to save one table. The two ledgers stay separate; a combined *read-side*
  admin view (union both tables into one feed) was considered and deferred, not built here.
- **The log write is best-effort and isolated from the decrement's own success.** It sits in its own
  inner `try`/`catch` inside `ConsumeQuotaAsync`, after the guarded `ExecuteUpdateAsync` has already
  committed directly (that call isn't part of a `SaveChanges` unit of work, so it's already durable
  the moment it returns rows-affected). If the log insert itself throws, the exception is logged and
  swallowed — `ConsumeQuotaAsync` still reports `Consumed = true`. The alternative (letting a logging
  failure bubble up and fail the whole call) would make the *caller* (`GameService.SpendPointsAsync`)
  wrongly fall back to charging wallet points on top of a quota unit that was already spent — worse
  than a merely-incomplete reporting trail.
- **`GET admin/subscriptions/usage`** (paged, filterable by `userId`/`featureCode`/`fromDate`/
  `toDate`) — the raw event log, `SubscriptionUsageEventDto` per row (who, which subscription paid
  for it, which feature, how much, when). Same specification-composition pattern as `GET
  admin/subscriptions/users` (`UserIdEqualsSpecification`, plus two new specs:
  `ConsumptionLogFeatureCodeEqualsSpecification`, `ConsumptionLogDateRangeSpecification`).
- **`GET admin/subscriptions/usage/aggregate`** (`fromDate`/`toDate` required, `userId` optional) —
  per-feature totals (`TotalAmount`, `EventCount`, `DistinctUserCount`) for the date range, grouped by
  `Feature`. The same endpoint serves both a per-user usage panel (pass `userId`) and a global usage
  dashboard (omit it) — there was no need for two separate endpoints, only whether the query is
  filtered to one user.
- **Self-service is unaffected.** `GET subscriptions/me`'s live `limit`/`used`/`remaining` snapshot
  and `GET subscriptions/me/history`'s past-subscriptions list (see above) already cover what a
  regular user needs to see; this log and its two endpoints are admin-only (`GetUsageHistoryAsync`/
  `GetUsageAggregateAsync` live on `ISubscriptionService`, gated the same
  `[Permission(Roles = [nameof(Role.Admin)])]` as the rest of the admin `SubscriptionsController`).

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

### Gateway cleanup on batch expiry (fixed 2026-09-03)

Found live in the sandbox: a customer whose subscription history showed old rows
(short-interval Alpha/GamaTest test plans) already `Expired` but still being charged daily
on Stripe, completely decoupled from any locally-visible `Active` subscription. Root cause:
`ExpireOverdueSubscriptionsAsync` only ever flipped the local `Status` column — it never told
the gateway to stop. A genuinely-cancelled recurring subscription is expired the *other* way
(Stripe fires `customer.subscription.deleted`, handled by
`PaymentService.HandleRecurringWebhookAsync`'s `SubscriptionEnded` case, which calls
`CancelSubscriptionAsync`) — but a recurring subscription only reaches
`ExpireOverdueSubscriptionsAsync` at all because its `invoice.paid` renewal webhook was
missed, delayed, or failed to resolve `UserSubscriptionId` (normally that webhook keeps
`ExpirationDate` ahead of "now" indefinitely, so this job never sees it). Left alone, the
gateway side kept auto-charging on its own schedule forever, since nothing else in this
codebase reconciles that direction.

- `ExpireOverdueSubscriptionsAsync` now reads `ExternalSubscriptionId`/`Gateway` for the
  rows it's about to expire (same `t.Payments.Select(p => p.Gateway).FirstOrDefault()`
  pattern `SubscriptionService` already uses elsewhere) and, for each recurring one, calls
  `IRecurringPaymentGatewayProvider.TerminateSubscriptionAsync` — the same immediate-cancel
  call the admin revoke flow uses, not the period-end `CancelSubscriptionAsync`, since we've
  already decided locally that this subscription's period is over.
  `SubscriptionQuotaService` now takes `Lazy<IGenericFactory<IRecurringPaymentGatewayProvider,
  PaymentGateway>>` to resolve the provider, same as `SubscriptionService`.
- **Best-effort, deliberately isolated from the local expiry** — a gateway call failing here
  must never roll back or block the `Status = Expired` update; a stray still-billing
  subscription is a lesser problem than a stuck-`Active` local record blocking a user whose
  access was already correctly cut off.

### Follow-up: reconciling against the gateway before expiring, not just after (2026-09-05)

The 2026-09-03 fix above closed the "still billing after local expiry" gap, but its own
unconditional "overdue means terminate" logic opened a smaller one in the other direction:
found live the same day, tracing a single sandbox subscription's `Payments` history, a
perfectly healthy Daily recurring subscription's `invoice.paid` webhook arrived **consistently
~1 hour after** the naive per-interval expectation, every single cycle — not a one-off delay
(confirmed via nginx's own access log: one clean webhook delivery each time, no earlier failed
attempt to explain it as a retry) but the gateway's own real billing anchor simply differing
slightly from what `ActivateSubscriptionAsync` assumed at purchase time. Nothing about that is
wrong or exploitable — Stripe billed the customer correctly, on schedule, from its own real
anchor — but it means "overdue by any amount" is not a safe signal that a recurring
subscription has actually lapsed. Left as the 2026-09-03 fix shipped it, a long enough delay
(or an outage on either side) could make this job **terminate a Stripe subscription that was
never actually a problem**, cutting off and un-billing a paying customer on nothing more than
a late webhook.

- **A grace period, `SubscriptionQuotaService.OverdueGracePeriod` (6 hours)**, before a
  subscription is even queried as a candidate — `ExpireOverdueSubscriptionsAsync`'s own
  `WHERE` clause now checks `ExpirationDate < now - GracePeriod`, not `< now`. Ordinary
  webhook jitter like the case above resolves on its own well within this window without the
  job ever needing to ask the gateway anything. Since the query filters at the database level
  first, this doesn't add load proportional to total subscriber count — only however many
  rows are *already* more than 6 hours overdue even get loaded into memory, which should be
  zero or near-zero on any healthy night.
- **For a recurring subscription that's still overdue past the grace period, the job now asks
  the gateway directly** (`IRecurringPaymentGatewayProvider.GetSubscriptionStatusAsync`,
  new — reads Stripe's own `Subscription.Status` and (since `CurrentPeriodEnd` moved from the
  top-level `Subscription` to each `SubscriptionItem` in this SDK version) the first item's
  `CurrentPeriodEnd`) **before** deciding anything:
  - Gateway confirms `active` with a period end still in the future → **self-heal, don't
    expire**: `ISubscriptionQuotaService.SyncExpirationFromGatewayAsync` (new) sets
    `ExpirationDate` directly to the gateway's own reported value (not "+1 `BillingInterval`"
    computed from the stale local one — a direct set catches all the way up in one call even
    if more than one cycle was missed, where "+1 interval" would only ever catch up one cycle
    per run) and resets quota, exactly like a real renewal would have. Deliberately does not
    apply a pending plan switch the way `RenewSubscriptionAsync` does — a pending switch
    combined with a missed-webhook reconciliation is a rare-in-rare edge case, picked up by
    the next genuine renewal instead. **Also records the recovered cycle's `Payment`** — keyed
    by the gateway's own current invoice id (Stripe: `Subscription.LatestInvoiceId`, threaded
    through `SubscriptionStatusResponseDto.LatestInvoiceId`), using the exact same
    `(TransactionId, Gateway)` idempotency guard `PaymentService.HandleInvoicePaidAsync` already
    relies on everywhere else. This isn't just for the admin `payments` report's sake — it's
    what makes reconciliation safe against the webhook that "went missing" actually being a
    **delayed retry that arrives for real later** (found worth asking about live: Stripe does
    retry a failed delivery, with backoff over days): without this guard, a late-arriving
    original webhook for a cycle reconciliation already caught up would insert its own new
    `Payment` (not yet recorded, so not caught as a duplicate) and call `RenewSubscriptionAsync`
    again on a still-`Active` subscription — double-extending `ExpirationDate` and wiping out
    quota usage made in the days in between. With the guard, that same later insert collides on
    the identical invoice id, is caught exactly like a redelivered webhook already is, and
    correctly skips renewing a second time. Degrades to syncing without recording a `Payment`
    (and without this dedup protection) only if the gateway itself reports no current invoice
    id at all — not expected in practice for a genuinely active recurring subscription.
  - Gateway confirms it's genuinely over (`canceled`/`unpaid`/`past_due`/...) → proceed exactly
    as the 2026-09-03 fix did: best-effort `TerminateSubscriptionAsync`, then expire locally.
    `past_due` deliberately counts as "not confirmed active" here, matching "Dunning is
    entirely Stripe's" above — a subscription mid-retry is already, by design, usable only
    until its own `ExpirationDate` regardless of gateway status, so this reconciliation must
    agree, not carve out a special case for "still retrying."
  - **The gateway check itself fails** (network error, gateway downtime) → leave the
    subscription `Active`, re-check next run. A stuck-`Active` row for one more day is a far
    smaller problem than wrongly cancelling a paying customer's real, still-billing
    subscription on an unconfirmed guess.
- **New admin action, `POST admin/subscriptions/users/{id}/resync`** — the source-of-truth
  counterpart to the existing `extend` (which just pushes `ExpirationDate` forward by a
  guessed day count, never resets quota, never touches the gateway). Runs the exact same
  gateway-status-then-sync check on demand, for a support case, without waiting for the
  nightly job's own grace period to elapse. `synced: false` (not an error) whenever the
  gateway doesn't confirm active-with-future-period-end — `gatewayStatus` carries the raw
  reason so the admin can see why without a separate gateway-dashboard lookup, and can follow
  up with `revoke` if they agree it should be cut off locally too, rather than this action
  silently doing that itself. `NotValid`/`SubscriptionNotRecurring` for a one-time/GamaTrain
  subscription — nothing to ask a gateway about; use `extend` for those.

### Quota preserved (not reset) across an immediate plan switch (fixed 2026-09-03)

`ApplyPlanSwitchAsync` (the immediate/upgrade path) re-snapshots quota buckets via
`CreateQuotasAsync`, which used to always start every bucket's `Used` at `0`. That's correct
at a real renewal boundary, but an immediate switch only bills the *prorated remaining-period*
difference (see "Plan upgrade/downgrade with proration" above) — not a new period — so wiping
consumption already made this period as a side effect of switching plans mid-cycle handed out
free extra quota, repeatably, on every switch.

- `CreateQuotasAsync` gained a `preserveUsage` parameter (default `false`, unchanged behavior
  for `ActivateSubscriptionAsync`/`GrantSubscriptionAsync`/the deferred-switch-at-renewal
  branch inside `RenewSubscriptionAsync` — all three really are fresh periods). `Apply
  PlanSwitchAsync` passes `true`: each new bucket's `Used` is carried forward from whichever
  old bucket(s) covered any of the same `FeatureId`s (`Max` across old buckets when a
  FeatureId's old usage came from more than one, since a single old `Used` can legitimately
  apply to several pooled FeatureIds), capped to the new bucket's own `Limit` so `Remaining`
  never goes negative.
- Feature composition/pooling can differ between old and new plan (a FeatureId present in one
  plan but not the other simply starts at `0`, same as before) — this only ever carries usage
  forward for features the new plan still grants.

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
- **Trial periods.** `StripePaymentGatewayProvider.CreateSubscriptionCheckoutAsync`/`VerifyAsync`
  only support a real (non-trial, non-$0) recurring price - a code comment there calls this out as
  its own backlog item, but (found 2026-08-14, while auditing webhook event coverage) that item was
  never actually written down anywhere in this repo until now. If ever built, it would need at least
  `customer.subscription.trial_will_end` wired into `RecurringWebhookEventType`/
  `ParseWebhookEventAsync` (a new event type, same shape as `PaymentFailed` above), plus deciding how
  a trial's eventual first real charge should map onto the existing `subscription_create`-is-ignored/
  `subscription_cycle`-renews split in `HandleInvoicePaidAsync`.
