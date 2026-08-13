# Content Delivery & Owner Commissions

Business logic: `src/Application/Service/ContentDeliveryService.cs`. Contract:
`src/Application/Interface/IContentDeliveryService.cs`. Provider:
`src/Infrastructure/Interface/IContentDeliveryProvider.cs`,
`src/Infrastructure/Infrastructure/Provider/ContentDelivery/GamaApiContentDeliveryProvider.cs`.
Entity: `src/Domain/Entity/ContentOwnerCommission.cs`. Controller:
`src/Presentation/Api/Controllers/DownloadsController.cs`. See
`docs/business/payments-and-points.md` for the points ledger this feature charges against, and
`docs/business/subscriptions.md` for the quota system it tries first.

## The core idea

Some downloadable content (gama-api's legacy PastPaper files) has an owner — the user who
originally uploaded it — and gamatrain-back now acts as the accountant for a commission owed to
that owner whenever someone else pays to download it. This is a genuinely new domain, not a
`GameService` concern: `GameService.SpendPointsAsync` still only knows how to charge a user for a
`ContentType`; it has no notion of an external source, a download URL, or an owner.
`ContentDeliveryService` sits in front of it, orchestrating three things behind one API call:

1. Check price/paid status first via a side-effect-free gama-api *detail* call — a separate
   endpoint from the one that actually serves the file (see "Two gama-api calls per content type"
   below). The one exception is PastPaper's `extra` file type, not broken out by its detail
   endpoint, which falls straight to step 2 using whatever the download endpoint itself reports.
2. Resolve the download from its source, charging the downloader first if a charge is needed and
   only fetching/serving the URL once that's settled (or once gama-api already shows it paid, or
   it's genuinely free).
3. Accrue a commission for the content's owner, only if the source reports an owner **and** a
   charge actually happened this call.

## One endpoint, three content types, three gama-api endpoint *pairs*

`POST api/v1/downloads` takes a `DownloadContentType` field — exactly `PastPaper`, `Multimedia`, or
`Exam` — that selects which pair of gama-api endpoints to call: a side-effect-free *detail*
endpoint first, then a download endpoint. All three content types are chargeable, but only
PastPaper's download endpoint reports an owner this feature can accrue commission against (see
"Commission accrual" below):

| `DownloadContentType` | Detail endpoint (price/paid check) | Download endpoint (URL + charge trigger) | `FileType`/`ExtraId`? |
|---|---|---|---|
| `PastPaper` | `GET /tests/{id}` → `data.files.{pdf,word,answer}.{price,paid}` | `GET /tests/download/{id}/{type}[/{extraId}]` → `{url, name, ownerUID, price:{price,paid}}` | Yes — `FileType` required (`pdf`/`word`/`answer`/`extra`), `ExtraId` only for `type=extra` |
| `Multimedia` | `GET /files/{id}` → `data.files.{price,paid}` (flat, one file per item) | `GET /files/download/{id}` → `{url, name}` only | No |
| `Exam` | `GET /exams/{id}` → `data.price.pdf.{price,paid}` (`data.price.participation` is a different, unrelated action — taking the exam, not downloading it) | `GET /exams/download/{id}` → `{url, name}` only | No |

Confirmed live against real gama-api data (not just its `openapi.yaml`, which under-specifies
response bodies for every endpoint in this table): none of the three *download* endpoints beyond
`/tests/download` report an owner, and none of them report price/paid either — `/files/download`
and `/exams/download` return only `{url, name}`. `GetDownloadUrlResponseDto.OwnerExternalId`/
`Points`/`Paid` are therefore all nullable, and `ContentDeliveryService` treats their absence as the
signal to skip charging/commission entirely from *that* response — not an error. This is why the
price/paid decision is made from the *detail* endpoints instead (see below), not the download
endpoints' own (mostly absent, and for PastPaper untrustworthy) reporting.

### `DownloadContentType`, not the broader `ContentType`

`src/Domain/Enumeration/DownloadContentType.cs` is a **dedicated 3-member enum** for this feature,
deliberately separate from the pre-existing `ContentType` (`src/Domain/Enumeration/ContentType.cs`)
used by the unrelated `games/spends` endpoint (`GameService.SpendPointsAsync`, unchanged by this
feature — still charges `FeatureCodes.PastpaperDownload`/`TestDownload` and
`TransactionType.DownloadPastPaper`/`DownloadTest` for its own `PastPaper`/`Test` distinction; a
subscription plan can and does grant those two different quota limits, so this feature does not
touch that logic at all).

Using a narrower type here — rather than reusing `ContentType` and rejecting `Test` at runtime —
means the **Swagger schema itself** only ever advertises the 3 supported values; a client can't
even construct a request naming `Test`, and one that tries fails at model binding (`[Required]` on
a `DownloadContentType?` that a `Test` string can't parse into) rather than needing a bespoke
validation error. `ContentType.Test` (and `TransactionType.DownloadTest`, which it's paired with in
`games/spends`) remain defined on the broader enum only because migration
`20260621193350_TransactionType.cs` compiles a reference to both by name in a historical
data-backfill statement — migrations are immutable, so neither can ever be removed, but this
feature simply never references either.

`ContentDeliveryService.MapContentType` maps all three `DownloadContentType` members to their
`ContentType` counterpart (`PastPaper`→`PastPaper`, `Multimedia`→`Multimedia`, `Exam`→`Exam`) when
calling `GameService.SpendPointsAsync` and when writing `ContentOwnerCommission.ContentType` — a
hardcoded, provably-correct mapping, expressed as a `switch` with `when` guards on `==` comparisons
(not a plain if-chain or a nested ternary — this repo's analyzer set (`IDE0046` vs `S3358`) rejects
both of those shapes for a 3-way smart-enum branch, so the `switch`/`when` form is the accepted
idiom here; see the identical shape in `GameSerivce.MapContentType` and
`GamaApiContentDeliveryProvider.GetContentPriceStatusAsync`).

## Two gama-api calls per content type: a side-effect-free price check, then the download itself

Every one of gama-api's three download endpoints (`GET .../download/...`, called via
`GetDownloadUrlAsync`) has a real, confirmed-live side effect for PastPaper (and is assumed to,
for consistency, for Multimedia/Exam too — see below): gama-api appears to mark the download
paid/delivered for that user as a consequence of merely serving the URL once — **independent of
whether gamatrain-back's own points/quota system ever actually charged the user.** An earlier
revision of this code (fixed 2026-07-20) treated `/tests/download`'s own `price: { price, paid }`
field as a free-pass for PastPaper — skip the charge whenever `paid == true` — which was a real,
exploitable bug: a user with **zero balance and no subscription** could call `POST
api/v1/downloads` twice for the same content. The first call correctly failed with
`InsufficientBalance` (nothing charged), but that call's `GetDownloadUrlAsync` request had already
flipped gama-api's own `paid` state as a side effect of serving the URL — so the *second*, identical
call then saw `paid == true` and returned the file for free (`Spent: false`, `PaidBy: null`,
`succeeded: true`), with no charge or quota consumption at any point.

The fix isn't to stop trusting gama-api's payment state altogether (that would mean re-charging a
user every single time they re-download something they've already legitimately paid for, since
gamatrain-back keeps no ledger of *which specific file* a user has bought — see
"Deliberately separate from the points wallet..." below for why that's not duplicated here either).
Instead, gama-api turns out to expose the same `price`/`paid` information through a **second,
separate, side-effect-free endpoint per content type** — its *detail* page, not its download URL
(see the table above) — confirmed live (2026-07-20, for all three) to leave `paid` unchanged no
matter how many times it's called, unlike the download endpoints, which are the *only* place `paid`
legitimately changes (as a result of gamatrain-back actually calling one after payment is settled).

`ContentDeliveryService.DownloadWithPriceCheckAsync` therefore calls the matching detail endpoint
**first**, for every `DownloadContentType` except PastPaper's `extra` file type (not broken out by
`GET /tests/{id}`, which only has `pdf`/`word`/`answer` keys):

- `paid == true` or `price` is `0`/absent → fetch the URL via the download endpoint and return it,
  no charge attempted (mirrors gama-api's own settled/free state; the download endpoint's own `paid`
  flag is irrelevant here since nothing needed to be decided from it).
- `paid == false` and `price > 0` → charge locally first (`GameService.SpendPointsAsync`,
  quota-then-wallet, same as before). **Only on a successful charge** does the code call the
  download endpoint — so a failed charge can never reach (and taint) the endpoint that flips
  gama-api's `paid` state. This is what actually closes the exploit: the side-effecting call is
  simply never made until payment is confirmed.

`GamaApiContentDeliveryProvider.GetContentPriceStatusAsync` dispatches to one of three private
methods (`GetTestPriceStatusAsync`/`GetFilePriceStatusAsync`/`GetExamPriceStatusAsync`) based on
`DownloadContentType`, calling `Core:TestDetails`/`Core:MultimediaDetails`/`Core:ExamApiDetails`
respectively (`https://core.gamatrain.com/api/v1/{tests,files,exams}/{0}`, alongside the existing
`Core:TestDownload`/`Core:FileDownload`/`Core:ExamDownload`). `DownloadWithoutPriceCheckAsync` is
the fallback path for the one case this price check doesn't cover — PastPaper's `extra` file type —
and it still never reads the download endpoint's own `paid` flag as a reason to skip charging, for
the same reason described above.

## Provider layer: `IContentDeliveryProvider`

Follows this repo's standard external-integration shape (`docs/architecture/design-patterns.md`
§4): `IContentDeliveryProvider : IProvider<ContentSource>`, resolved through
`IGenericFactory<IContentDeliveryProvider, ContentSource>`. `ContentSource`
(`src/Domain/Enumeration/ContentSource.cs`) has one seeded member today, `GamaApiLegacy` — kept as
a smart enum (not a bool/hardcoded branch) specifically so a second content *source* (a different
external system entirely) can be added later as a new provider implementation + enum member,
mirroring how `Payment.Gateway` has room for a future `PayPal` member. This is a different axis
from `DownloadContentType` above: `ContentSource` picks *which provider*, `DownloadContentType`
picks *which URL within that provider* — `GamaApiContentDeliveryProvider.GetDownloadUrlAsync`
dispatches to one of the three gama-api endpoints internally based on the request's
`DownloadContentType`.

`GamaApiContentDeliveryProvider` calls gama-api with the **downloading user's own legacy JWT** in
the `Authorization` header — not a service-level credential, because gama-api prices/gates per
caller (the `price.paid` field, where it exists, is scoped to whoever's token made the call). This
means `DownloadsController.Download` can only serve a caller whose current request already carries
that JWT (i.e. a legacy-auth-bridge session, see `docs/api/authentication.md`) — reading it
straight from the incoming `Authorization` header via `TokenAuthenticationHandler.GetTokenFromHeader`,
the same mechanism `legacy-auth/logout` uses. A caller on a native session (Identity cookie or this
app's own opaque bearer token) has no live gama-api credential for this backend to present on their
behalf, so the provider call fails cleanly (mapped to a generic error) rather than silently
skipping the owner-payment check.

gama-api's real `/tests/download` route accepts either 2 segments (`/tests/download/{id}/{type}`)
or 3 (`.../{extraId}`) — `extraId` is appended only when supplied, not always required despite the
3-segment shape shown in gama-api's `openapi.yaml`.

## Charge: quota-then-points, now for all three content types

`ContentDeliveryService`'s price-check path skips the charge (and returns `Spent = false`) whenever
the detail endpoint reports `Points` as `null`/`0` (a real gama-api response can report a genuine
zero price — a zero-cost `SpendPointsAsync` call would still write a pointless zero-amount
`Transaction` row, confirmed live and fixed by short-circuiting before calling it) or `paid == true`.
Otherwise it calls the existing `IGameService.SpendPointsAsync` — the same quota-then-points logic
used by `games/spends` — with `Points` set to **gama-api's own reported price**, not a
client-supplied amount. This is a deliberate hardening over the plain `games/spends` endpoint (which
still trusts whatever `Points` the caller sends): since this flow already calls gama-api and gets an
authoritative price, there's no reason to trust the client for it here. If the charge fails (no
quota, insufficient points), the whole download fails — the URL is not returned, since it was
never paid for by either side.

**Multimedia and Exam downloads are now charged too** (fixed 2026-07-20, alongside the exploit
above) — previously unconditionally free through this endpoint, since their download endpoints
never reported a price; their *detail* endpoints do (see the table above).
`GameService.SpendPointsAsync`'s `ContentType`→`(FeatureCode, TransactionType)` mapping
(`GameSerivce.MapContentType`) was widened from a `PastPaper`-or-`Test` ternary to all four
`ContentType` members: `FeatureCodes.MultimediaDownload`/`ExamDownload` (new constants,
`src/Domain/Enumeration/FeatureCodes.cs`) and `TransactionType.DownloadMultimedia`/`DownloadExam`
(new members, values `13`/`14`, `src/Domain/Enumeration/TransactionType.cs` — no migration needed,
`TransactionType` is a plain `smallint` column, not a lookup table). **Deliberately no `Features`
catalog row exists yet for these two codes** (unlike `PastpaperDownload`/`TestDownload`, which do) —
a migration seeding them was written and then deliberately dropped, since the wallet-points charge
path works correctly either way: `SubscriptionQuotaService.ConsumeQuotaAsync` degrades gracefully
when a `Feature.Code` has no catalog row (no matching quota rows exist, so it always reports
"not consumed" and falls through to the wallet), so an unseeded code just means no subscription plan
can grant free quota for these two yet — not a bug, and one INSERT away from being addable later
(mirror `20260710140837_AddSubscriptionQuotaEntities.cs`'s seed block) if that's ever wanted.

On an insufficient-balance failure specifically, `DownloadContentResponseDto`/
`DownloadContentResponseViewModel.UpgradeSuggestions` carries through the same plan-centric list
(plus the `AvailableBillingIntervals` manifest) that `games/spends` v2 already exposes — one entry
per plan whose `SubscriptionPlanFeature.Limit` would cover this feature, each with up to the 3
cheapest qualifying `Prices` nested per billing interval, per
[`docs/business/subscriptions.md`](subscriptions.md) — so the download endpoint can drive the same
"upgrade/top-up" UI, with a period toggle and a short pick of tiers within it, instead of a
dead-end error. Also, `Localizer["InsufficientBalance"]`
(`GameService.SpendPointsAsync`) now has a real resx entry
(`src/Core/Resource/Application/GameService.resx`) — previously it had none anywhere in the repo and
silently rendered as the literal string `InsufficientBalance` to callers, including this endpoint.

## Commission accrual

Only runs when `OwnerExternalId` is reported **and** the downloader's charge above actually
succeeded. In practice this still only ever fires for `PastPaper` today: `OwnerExternalId` comes
from the *download* endpoint's response (`GetDownloadUrlResponseDto.OwnerExternalId`, populated
only by `/tests/download`'s `ownerUID` field), not the detail endpoint used for the price check —
`/files/download`/`/exams/download` report no owner at all, confirmed live, so Multimedia/Exam
downloads are now charged but never accrue commission, even though their *detail* endpoints do
expose an owner-adjacent identity (`ownerIdentity`, in two more formats — a bare numeric id for
files, not present at all in the one exam response sampled) that this feature does not currently
read or resolve. Steps
(`ContentDeliveryService.AccrueCommissionAsync`):

1. Resolve gama-api's `ownerUID` (a `CoreId`) to a local `ApplicationUser`. **If unresolved (the
   owner never linked/created a local account), the download still succeeds for the downloader —
   commission is silently skipped, not an error.** This is a deliberate choice: a data gap on the
   owner side must never block a legitimate, already-paid-for download.
2. Read `ApplicationSettingsDto.ContentOwnerCommissionPercent` (admin-editable, default `20`) and
   compute `AmountUsd = Points * Percent / 100 / 100` — the final `/ 100` is a **fixed** points-to-USD
   rate (100 points = $1), a first-phase simplification, not yet admin-configurable and not routed
   through `ICurrencyConverterProvider` (that provider only converts currency → points for the
   unrelated top-up flow; there is no points → currency conversion anywhere else in this codebase).
3. Insert one `ContentOwnerCommission` row, snapshotting `Points`, `CommissionPercent`, and
   `AmountUsd` at accrual time — a later admin edit to the percent never changes already-accrued
   rows, same snapshot discipline as `UserSubscriptionQuota.Limit` and `Payment.ExchangeRate`.

Commission accrual failing (e.g. a transient DB error) never fails the download or un-charges the
downloader — it's logged and swallowed, since the downloader has already been charged by that
point.

## Deliberately separate from the points wallet and subscription quota

A content owner's commission balance is **not** a `Transaction`, not mixed into
`ApplicationUser.CurrentBalance`, and not a `UserSubscriptionQuota`. It only exists as the sum of
that owner's `ContentOwnerCommission` rows — there is no denormalized balance column anywhere. This
was an explicit requirement: commission is owed money, not spendable in-app points, and keeping it
in its own ledger avoids ever letting it be spent as points by accident.

## `Reason` vs `Source`: two separate axes, on purpose

`ContentOwnerCommission.Source` (`ContentSource`) answers "which external system served this
content" — relevant to downloads specifically. `ContentOwnerCommission.Reason`
(`CommissionReason`, `src/Domain/Enumeration/CommissionReason.cs`) answers "what kind of event
earned this row" and is deliberately a separate enum, because a future commission reason (e.g. a
bonus for publishing a blog post) may not involve an external content source at all. Only one
`Reason` exists today (`ContentDownload` — renamed from `LegacyContentDownload` 2026-07-14, since
the "Legacy" prefix mislabeled intent: `Source` already carries which system served the content,
and `gama-api` is meant to stay as one of potentially several permanent content sources rather than
being retired, unlike the temporary legacy-auth bridge); the download-specific columns on
`ContentOwnerCommission` (`ExternalContentId`, `ExternalFileType`, `ExternalExtraId`,
`ContentType`, `DownloaderUserId`) are scoped to that one reason and will need to be widened (e.g.
made nullable, or split into a per-reason detail table) once a second reason is actually built —
not attempted speculatively now, since a guess at that shape without a concrete second use case
would likely be wrong.

## Commission report (read-only, no payout)

Two list endpoints report accrued `ContentOwnerCommission` rows — both read-only, since there is
no paid/payout state on the entity yet (see below). Deliberately a separate `CommissionsController`
rather than nested under `DownloadsController` — commissions are earned via a `Reason`
(`ContentDownload` today), and `Reason`/`Source` are already kept apart specifically so a future
commission event (e.g. viewing content, exam participation) doesn't have to be shaped as a
"download" at the API surface, even though downloads are the only reason today:
- `GET commissions` (`CommissionsController`, `User`) — the caller's own commissions only,
  forced via `OwnerUserIdEqualsSpecification(User.UserId())` at the controller layer (the caller
  can never pass another owner's id — there is no `OwnerUserId` field on this endpoint's request
  view model at all, not just an ignored one, precisely to avoid the class of bug seen in
  blog-contributions filtering, where a specification was silently overwritten instead of
  combined).
- `GET admin/commissions` (`Areas/Admin/Controllers/CommissionsController`, `Admin`) — every
  owner's commissions, optionally narrowed to one via `ownerUserId`.
- Both share `IContentDeliveryService.GetContentOwnerCommissionsAsync` and the same
  `ContentOwnerCommissionListResponseViewModel` — filtering by `startDate`/`endDate` on both,
  ownership scoping is the only difference, enforced in each controller rather than the shared
  service.

## Deliberately out of scope for this phase

- **Payout.** Crossing `ApplicationSettingsDto.ContentOwnerCommissionPayoutThresholdUsd`
  (admin-editable, default `$100`) triggers nothing yet — there is no payout mechanism (Stripe is
  the intended rail, per 2026-07-14 direction, likely alongside other methods; not built yet), and
  `ContentOwnerCommission` intentionally carries no paid/payout-status column. This is explicitly a
  separate, later phase — the report endpoints above are read-only and don't anticipate it.
- **A real points↔currency exchange rate.** The fixed 100-points-per-$1 rate is a first-phase
  simplification, same spirit as `Payment.BaseCurrencyAmount`'s pragmatic 1:1 stablecoin peg
  (`docs/business/payments-and-points.md`) — not a real FX source.
- **A second `ContentSource` or `CommissionReason`.** The provider/factory and the `Reason`/`Source`
  split exist so these are additive later (new enum member + new provider implementation, or a
  schema widening for a non-download reason) — neither is built speculatively now.
- **Subscription-plan quota for Multimedia/Exam downloads.** The `FeatureCodes.MultimediaDownload`/
  `ExamDownload` codes exist and charging works, but no `Features` catalog row is seeded for them
  yet, so no `SubscriptionPlanFeature`/`UserSubscriptionQuota` can reference them — every
  Multimedia/Exam download charges wallet points directly today, never quota. Adding that is a
  small, additive, data-only migration (see "Charge" above) — not done now since it wasn't asked
  for.
- **Commission accrual for Multimedia/Exam.** Charged, but never accrues commission — see
  "Commission accrual" above for why (their download endpoints report no owner).
