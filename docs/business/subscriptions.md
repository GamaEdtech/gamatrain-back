# Subscriptions

Business logic: `src/Application/Service/SubscriptionService.cs` (plan/feature/price
definitions, purchase orchestration), `SubscriptionQuotaService.cs` (activation, quota
consumption, expiry). Contracts: `src/Application/Interface/ISubscriptionService.cs`,
`ISubscriptionQuotaService.cs`. Entities: `src/Domain/Entity/SubscriptionPlan.cs`,
`Feature.cs`, `SubscriptionPlanFeature.cs`, `SubscriptionPlanPrice.cs`,
`SubscriptionPlanGatewayMapping.cs`, `UserSubscription.cs`, `UserSubscriptionQuota.cs`.
See `docs/database/schema.md` for the column-level reference and
`docs/business/payments-and-points.md` for the points ledger / payment-gateway machinery
this feature builds on.

## The core idea: quota, not currency

A plan grants **fixed, named allowances per feature** — e.g. plan "Alpha" grants 500
pastpaper downloads, 100 test submissions, 100 exam participations for its billing
period. This is deliberately **not** a points top-up: buying a plan never runs the
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
  Seeded codes (`src/Domain/Enumeration/FeatureCodes.cs`): `PastpaperDownload` (wired, see
  below — also charges `ContentType.Test` downloads since 2026-07-13, see
  `docs/business/content-delivery.md`'s "Test merged into PastPaper" note), `TestDownload`
  (defined for historical `Transaction`/quota-consumption data only — no code path writes it
  anymore), `TestSubmission`, `ExamParticipation` (seeded `IsActive = false` — cataloged for
  future use, no call site charges them yet).
- **`SubscriptionPlan`** — definition only: `Title`, `BillingInterval`, `IsActive`,
  `Highlight`, `Polygon` (geo region — controls whether the plan is shown to a user at
  all, independent of price). Carries **no price** — that was removed to
  `SubscriptionPlanPrice` (see below) precisely so multi-region pricing wouldn't require
  duplicating the plan's features/quotas per region.
- **`SubscriptionPlanFeature`** — `(SubscriptionPlanId, FeatureId, Limit)`. One row per
  feature a plan grants.
- **`SubscriptionPlanPrice`** — `(SubscriptionPlanId, CountryCode, Currency, Price)`.
  `CountryCode = NULL` is the **global default** price, and a unique index on
  `(SubscriptionPlanId, CountryCode)` guarantees at most one default row per plan (SQL
  Server treats `NULL` as a distinct value in unique indexes, so this doesn't collide
  with country-specific rows). Today every plan has exactly one price row (the default,
  in USD) — regional pricing is built but dormant, see below.
- **`SubscriptionPlanGatewayMapping`** — `(SubscriptionPlanPriceId, Gateway,
  ExternalProductId, ExternalPlanId)`. Keyed off the *price* row, not the plan, because
  gateway Product/Price objects (Stripe Prices, PayPal Plans) are currency-bound — a
  Turkey-TRY price and a US-USD price of the same plan need separate external ids. This
  table is written by admin today but **not yet read by anything** — it's reserved for
  a later native-recurring-billing phase (Stripe Subscriptions/webhooks); the current
  purchase flow is one-time checkout and doesn't need it.
- **`UserSubscription`** — one purchase/enrollment: `UserId`, `SubscriptionPlanId`,
  `Status` (`Pending`/`Active`/`Expired`/`Cancelled`), `CreationDate`, `StartDate`/
  `ExpirationDate` (set on activation), `PricePaid`/`Currency` (snapshotted at purchase —
  a later admin price edit never changes what an existing subscriber already paid). The
  link to the payment that paid for it is **`Payment.UserSubscriptionId`, not the reverse**
  — `UserSubscription` has no `PaymentId` column, which is what avoids a circular FK
  between the two tables.
- **`UserSubscriptionQuota`** — one row per `(UserSubscription, Feature)`: `Limit`
  (snapshotted from `SubscriptionPlanFeature` at activation time) and `Used`. `Remaining`
  is always computed (`Limit - Used`), never stored, so there's only one number to keep
  consistent under concurrent decrements.

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

## Purchase → verify → activate lifecycle

1. **`SubscriptionService.PurchaseSubscriptionAsync`** (`POST
   api/v1/subscriptions/plans/{id}/purchase`): validates the plan is active, resolves its
   price via `ResolvePriceAsync`, inserts a `UserSubscription` row (`Status = Pending`,
   `PricePaid`/`Currency` snapshotted), then calls the existing
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
   `StartDate = now`, `ExpirationDate = plan.BillingInterval.CalculateEndDate(start)`
   (the previously-unused `BillingInterval` helper), then does a **guarded** set-based
   update (`WHERE Status == Pending`) to flip the subscription to `Active` — zero rows
   affected means this activation already happened (e.g. a duplicate verify call), and
   the method fails cleanly without double-activating. It then snapshots one
   `UserSubscriptionQuota` row per active `SubscriptionPlanFeature` on the plan.

## Quota consumption and the points fallback

`SubscriptionQuotaService.ConsumeQuotaAsync(userId, featureCode, amount)`:

1. Selects a candidate quota row: an `Active`, non-expired subscription with
   `Used + amount <= Limit` for that feature (earliest-expiring subscription first, if a
   user happens to have more than one active plan — draining the soonest-to-lapse one
   first is a deliberate, if untested-in-the-UI, product choice).
2. Performs the decrement as a **guarded `UPDATE`** re-checking `Used + amount <= Limit`
   in the `WHERE` clause and inspecting rows-affected — this is what makes concurrent
   consumption safe without locking: two simultaneous requests against the last unit of
   quota can't both succeed, and the loser retries once against a fresh read before
   giving up.
3. On failure, classifies *why* (`NoActiveSubscription` / `FeatureNotInPlan` /
   `QuotaExhausted`) and looks up **upgrade suggestions** — active plans whose limit for
   that feature exceeds the user's current one — so the caller can surface an upsell
   rather than a bare error.

**`GameService.SpendPointsAsync`** (the existing `games/spends` endpoint, pastpaper/test
downloads) wires this in ahead of the wallet: it tries `ConsumeQuotaAsync` first
(always `FeatureCodes.PastpaperDownload` — `ContentType.PastPaper` and `.Test` are charged
identically since 2026-07-13); if consumed, the action succeeds with
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
  `PayPal` member added when that integration lands.
- **Native recurring billing.** The current purchase flow is one-time checkout, full
  stop — there is no auto-renewal, no saved payment method, no webhook receiver. When
  recurring billing is built, the design intent (agreed, not yet implemented) is to use
  each gateway's *native* subscription objects (Stripe Subscriptions/Billing, PayPal
  Billing Subscriptions) rather than hand-rolled off-session charging, driven by a new
  `IRecurringPaymentGatewayProvider`-shaped capability that the GamaTrain wallet gateway
  simply wouldn't implement. `SubscriptionPlanGatewayMapping` exists now specifically so
  that phase doesn't need a schema change to arrive.
- **A real FX source.** `Payment.BaseCurrencyAmount`/`ExchangeRate` (see
  `docs/business/payments-and-points.md`) use a pragmatic 1:1 peg for USD-stable
  currencies only; `SOL`/`GET` are left `null` pending an actual rate source.
- **In-house pastpaper file serving.** Delivery is proxied to a separate legacy backend;
  quota/charge enforcement happens here, the file itself doesn't. If that proxy is later
  replaced with in-house serving, it should sit behind a provider interface (mirroring
  `IPaymentGatewayProvider`'s `IGenericFactory` pattern) so the swap doesn't touch the
  quota-check code path.
