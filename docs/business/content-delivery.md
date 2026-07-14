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

1. Resolve the download from its source.
2. Charge the downloader, only if the source reports a price for this content and hasn't already
   marked it paid.
3. Accrue a commission for the content's owner, only if the source reports an owner **and** step 2
   actually happened.

## One endpoint, three content types, three gama-api endpoints

`POST api/v1/downloads` takes a `DownloadContentType` field — exactly `PastPaper`, `Multimedia`, or
`Exam` — that selects which of gama-api's three download-URL endpoints to call:

| `DownloadContentType` | gama-api endpoint | Needs `FileType`/`ExtraId`? | Reports `price`/`paid`? | Reports an owner? |
|---|---|---|---|---|
| `PastPaper` | `GET /tests/download/{id}/{type}[/{extraId}]` | Yes — `FileType` required (`pdf`/`word`/`answer`/`extra`), `ExtraId` only for `type=extra` | Yes | Yes (`ownerUID`) |
| `Multimedia` | `GET /files/download/{id}` | No | No | No |
| `Exam` | `GET /exams/download/{id}` | No | No | No |

Confirmed live against real gama-api data (not just its `openapi.yaml`, which under-specifies
response bodies for all three): `/tests/download` returns
`{url, name, ownerUID, price: {price, paid}}`; `/files/download` and `/exams/download` return only
`{url, name}` — no `ownerUID`, no `price` field at all. This is not a quirk of specific ids tested;
it's the actual shape of those two endpoints. `GetDownloadUrlResponseDto.OwnerExternalId`/`Points`/
`Paid` are therefore all nullable, and `ContentDeliveryService` treats their absence as the signal
to skip charging/commission entirely — not an error, and not something requiring per-type
special-casing at the service layer (see below).

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

`ContentDeliveryService` maps the one case that ever charges/accrues commission
(`DownloadContentType.PastPaper`, the only type gama-api reports a price for) to
`ContentType.PastPaper` when calling `GameService.SpendPointsAsync` and when writing
`ContentOwnerCommission.ContentType` — a hardcoded, provably-correct mapping (not a general
switch), since `Multimedia`/`Exam` structurally never reach that code path at all.

## Why this isn't gama-api's own `price.paid`

For `PastPaper`, gama-api's own `price: { price, paid }` field is its own legacy points-price and
whether *this specific caller* has already paid for *this specific item*, according to gama-api.
That's the signal this feature is built around, not something to route around: if `paid` is
already `true`, this backend does nothing — no charge, no commission, since gama-api already
considers the download settled. Only when `paid` is `false` does gamatrain-back own the payment
(via its own quota/points, not gama-api's). `Multimedia`/`Exam` never report a price at all, so
this branch never applies to them — they're unconditionally free to fetch through this endpoint
(no `SpendPointsAsync` call is made).

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

## Charge: quota-then-points, unchanged

`ContentDeliveryService.DownloadContentAsync` skips the charge (and returns `Spent = false`)
whenever `Points` is `null` (Multimedia/Exam), `Points` is `0` (a real gama-api response can report
a genuine zero price — a zero-cost `SpendPointsAsync` call would still write a pointless
zero-amount `Transaction` row, confirmed live and fixed by short-circuiting before calling it), or
`Paid` is `true`. Only when `Points` is reported, non-zero, and not yet `paid` does
`ContentDeliveryService.DownloadContentAsync` call
the existing `IGameService.SpendPointsAsync` — the same quota-then-points logic used by
`games/spends` — with `Points` set to **gama-api's own reported price**, not a client-supplied
amount. This is a deliberate hardening over the plain `games/spends` endpoint (which still trusts
whatever `Points` the caller sends): since this flow already calls gama-api and gets an
authoritative price, there's no reason to trust the client for it here. If the charge fails (no
quota, insufficient points), the whole download fails — the URL is not returned, since it was
never paid for by either side.

## Commission accrual

Only runs when `OwnerExternalId` is reported (`PastPaper` only — never `Multimedia`/`Exam`,
which report no owner at all) **and** the downloader's charge above actually succeeded. Steps
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
`Reason` exists today (`LegacyContentDownload`); the download-specific columns on
`ContentOwnerCommission` (`ExternalContentId`, `ExternalFileType`, `ExternalExtraId`,
`ContentType`, `DownloaderUserId`) are scoped to that one reason and will need to be widened (e.g.
made nullable, or split into a per-reason detail table) once a second reason is actually built —
not attempted speculatively now, since a guess at that shape without a concrete second use case
would likely be wrong.

## Commission report (read-only, no payout)

Two list endpoints report accrued `ContentOwnerCommission` rows — both read-only, since there is
no paid/payout state on the entity yet (see below):
- `GET downloads/commissions` (`DownloadsController`, `User`) — the caller's own commissions only,
  forced via `OwnerUserIdEqualsSpecification(User.UserId())` at the controller layer (the caller
  can never pass another owner's id — there is no `OwnerUserId` field on this endpoint's request
  view model at all, not just an ignored one, precisely to avoid the class of bug seen in
  blog-contributions filtering, where a specification was silently overwritten instead of
  combined).
- `GET admin/contentownercommissions` (`ContentOwnerCommissionsController`, `Admin`) — every
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
- **Charging Multimedia/Exam downloads.** gama-api reports no price for either, so they're
  unconditionally free through this endpoint today. If that changes (gama-api starts reporting a
  price, or an in-house price is layered on top), `FeatureCodes.MultimediaDownload`/`ExamDownload`
  and matching `TransactionType`s would need to be added — not done now since there's nothing to
  charge against yet.
