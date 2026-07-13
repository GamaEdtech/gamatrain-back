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

Some downloadable content (today: gama-api's legacy test/pastpaper files) has an owner — the user
who originally uploaded it — and gamatrain-back now acts as the accountant for a commission owed
to that owner whenever someone else pays to download it. This is a genuinely new domain, not a
`GameService` concern: `GameService.SpendPointsAsync` still only knows how to charge a user for a
`ContentType` (`PastPaper`/`Test`); it has no notion of an external source, a download URL, or an
owner. `ContentDeliveryService` sits in front of it, orchestrating three things behind one API call:

1. Resolve the download from its source.
2. Charge the downloader, if the source hasn't already accounted for payment itself.
3. Accrue a commission for the content's owner, only if step 2 actually happened.

## Why this isn't gama-api's own `price.paid`

gama-api's own `GET /tests/download/{id}/{type}[/{extraId}]` (bearer-authed, priced/gated **per
caller**) already returns a `price: { price, paid }` field — its own legacy points-price and
whether *this specific caller* has already paid for *this specific item*, according to gama-api.
That's the signal this feature is built around, not something to route around: if `paid` is
already `true`, this backend does nothing — no charge, no commission, since gama-api already
considers the download settled and double-charging would be wrong. Only when `paid` is `false`
does gamatrain-back own the payment (via its own quota/points, not gama-api's).

## Provider layer: `IContentDeliveryProvider`

Follows this repo's standard external-integration shape (`docs/architecture/design-patterns.md`
§4): `IContentDeliveryProvider : IProvider<ContentSource>`, resolved through
`IGenericFactory<IContentDeliveryProvider, ContentSource>`. `ContentSource`
(`src/Domain/Enumeration/ContentSource.cs`) has one seeded member today, `GamaApiLegacy` — kept as
a smart enum (not a bool/hardcoded branch) specifically so a second content source can be added
later as a new provider implementation + enum member, mirroring how `Payment.Gateway` has room for
a future `PayPal` member.

`GamaApiContentDeliveryProvider.GetDownloadUrlAsync` calls gama-api's endpoint with the
**downloading user's own legacy JWT** in the `Authorization` header — not a service-level
credential, because gama-api prices/gates per caller (the `price.paid` field is scoped to whoever's
token made the call). This means `DownloadsController.DownloadTest` can only serve a caller whose
current request already carries that JWT (i.e. a legacy-auth-bridge session, see
`docs/api/authentication.md`) — reading it straight from the incoming `Authorization` header via
`TokenAuthenticationHandler.GetTokenFromHeader`, the same mechanism `legacy-auth/logout` uses. A
caller on a native session (Identity cookie or this app's own opaque bearer token) has no live
gama-api credential for this backend to present on their behalf, so the provider call fails
cleanly (mapped to a generic error) rather than silently skipping the owner-payment check.

gama-api's real route accepts either 2 segments (`/tests/download/{id}/{type}`) or 3
(`.../{extraId}`) — `extraId` is appended only when supplied, not always required despite the
3-segment shape shown in gama-api's `openapi.yaml`.

## Charge: quota-then-points, unchanged

When `paid` is `false`, `ContentDeliveryService.DownloadTestAsync` calls the existing
`IGameService.SpendPointsAsync` — the same quota-then-points logic used by `games/spends` — with
`Points` set to **gama-api's own reported price** (`price.price`), not a client-supplied amount.
This is a deliberate hardening over the plain `games/spends` endpoint (which still trusts whatever
`Points` the caller sends): since this flow already calls gama-api and gets an authoritative price,
there's no reason to trust the client for it here. If the charge fails (no quota, insufficient
points), the whole download fails — the URL is not returned, since it was never paid for by either
side.

## Commission accrual

Only runs when `paid` was `false` **and** the downloader's charge above actually succeeded. Steps
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

## Deliberately out of scope for this phase

- **Payout.** Crossing `ApplicationSettingsDto.ContentOwnerCommissionPayoutThresholdUsd`
  (admin-editable, default `$100`) triggers nothing yet — there is no payout mechanism (Stripe
  transfer, bank details, admin-triggered action), and `ContentOwnerCommission` intentionally
  carries no paid/payout-status column. This is explicitly a separate, later phase.
- **A real points↔currency exchange rate.** The fixed 100-points-per-$1 rate is a first-phase
  simplification, same spirit as `Payment.BaseCurrencyAmount`'s pragmatic 1:1 stablecoin peg
  (`docs/business/payments-and-points.md`) — not a real FX source.
- **A second `ContentSource` or `CommissionReason`.** The provider/factory and the `Reason`/`Source`
  split exist so these are additive later (new enum member + new provider implementation, or a
  schema widening for a non-download reason) — neither is built speculatively now.
