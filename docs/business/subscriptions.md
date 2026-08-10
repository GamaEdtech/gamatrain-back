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
