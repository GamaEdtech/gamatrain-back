# Project Snapshot

> High-level, point-in-time view of the system's current state. Update this file whenever
> architecture, database structure, APIs, business rules, infrastructure, or major workflows
> change significantly — see the "Living documentation" section of [`CLAUDE.md`](CLAUDE.md).
>
> Last updated: 2026-08-19, branch `feat/admin-subscription-quota-status`.

## What this system is

GamaEdtech Backend is a layered ASP.NET Core (.NET 10) REST API for the Gamatrain ed-tech
platform. It serves: a crowdsourced school directory with multi-dimension parent reviews, a blog,
a curriculum/exam content model, a gamified points ledger, crypto (Solana) + Stripe payments, a
quota-based subscription system (separate from the points ledger — see
[`docs/business/subscriptions.md`](docs/business/subscriptions.md)), a support-ticket system, and a
proactive email nudge system (see [`docs/business/notifications.md`](docs/business/notifications.md)).
Full domain detail: [`docs/business/`](docs/business/).

## Architecture at a glance

Clean/onion-style layering: `Domain` (entities, smart enums, specifications) ← `Application`
(interfaces + services, `ResultData<T>` everywhere) ← `Infrastructure` (EF Core DbContext,
providers) ← `Presentation` (view models + ASP.NET Core API). A large in-house framework lives in
`Core/Common` (DI-by-attribute, generic `Startup<TUser,TRole>`, specification base classes,
`ApiResponse<T>` envelope). Full detail: [`docs/architecture/`](docs/architecture/).

Key conventions every contributor (human or AI) should already know before touching code:
- Services never throw to callers — they return `ResultData<T>` with an `OperationResult`.
- Query filtering is via composable `ISpecification<T>` classes, not ad-hoc LINQ in controllers.
- Dependencies are injected as `Lazy<T>` almost everywhere.
- External integrations (email, file storage, payment gateways, currency conversion) go through a
  provider interface + `IGenericFactory<TProvider, TEnum>`, keyed by a smart enum.
- **HTTP status codes do not reflect success/failure** — nearly everything returns `200 OK`; check
  the `succeeded`/`errors` fields in the JSON body. See
  [`docs/api/overview.md`](docs/api/overview.md#known-limitations).

## Data

~109 EF Core migrations, no baseline/squash yet. Migrations apply automatically at process
startup. Full entity catalog: [`docs/database/schema.md`](docs/database/schema.md). Notable
recent fix: the `ImportLocations` migration (bulk-seeds ~156k location rows from an embedded
resource) now runs in 1000-line SQL batches instead of one giant batch, to avoid SQL Server
"insufficient memory to compile" errors (701) on memory-constrained instances — see
[`docs/database/migrations.md`](docs/database/migrations.md).

## API

Versioned URL-segment routing (`api/v1/...`), three auth schemes (Identity cookie, custom opaque
bearer token, API key — no JWT), `ApiResponse<T>` JSON envelope on every action. Full endpoint
catalog: [`docs/api/endpoints.md`](docs/api/endpoints.md).

## Known risks / open issues (carried from a 2026-07-07 deep static review, `ANALYZE.md`, untracked)

These are real, current issues a new contributor should be aware of, not hypothetical:

- **Secrets are committed** in `src/Presentation/Api/appsettings.json` (email provider token,
  payment gateway API key, root API key). They need rotating and moving to environment
  variables/Key Vault; this has not been done yet. Never add new secrets to a tracked file.
- **Exception messages leak to API clients** — most controllers/services return raw
  `exception.Message` in the error response body.
- **Payment verification needs concurrency/authorization hardening** before it should be
  considered a stable, audited path for high-value transactions — see
  [`docs/business/payments-and-points.md`](docs/business/payments-and-points.md) (mechanism
  details intentionally kept out of this public repo; see the internal review).
- **Near-zero real test coverage, and the test suite doesn't currently pass as documented** — beyond
  being small and requiring a live database, `ApplicationDBContext` is registered `Transient` and
  swaps in a fresh random in-memory database on every resolution under the test harness, so no
  cross-call test assertion can pass; the documented `dotnet test` command currently fails even a
  pre-existing, unmodified test. See [`docs/development/testing.md`](docs/development/testing.md).
- **No CI test/lint gate** — all three deploy workflows build and deploy directly with no
  `dotnet test` step. See [`docs/deployment/ci-cd.md`](docs/deployment/ci-cd.md).
- **Pdf/Word exam export needs Chromium native libraries on the deploy target, not yet confirmed
  present on Azure Web App or either VPS** (2026-07-16, extended 2026-07-17 to Pdf — see
  `docs/business/exams-and-content.md` and `docs/deployment/overview.md`):
  `HeadlessBrowserRenderProvider` launches a headless Chromium (`chrome-headless-shell` via
  PuppeteerSharp) to render exam formulas (both Pdf/Word) and, as of 2026-07-17, to print the whole
  Pdf export via Chromium's native print engine too — needs ~20 native shared libraries (`libatk`,
  `libcups`, `libgbm`, `libasound`, etc. — see `docs/deployment/overview.md` for the full list) that
  a bare Linux App Service/VPS typically doesn't have preinstalled. If missing: **Word** formula
  rendering falls back to unrendered raw `$...$` text rather than crashing (degrades silently); but
  **Pdf now fails entirely** if Chromium can't launch, since Pdf generation itself depends on it, not
  just formulas. Needs verifying/installing on all three deploy targets before this is
  production-ready — more urgent now than when only Word formulas depended on it.
- **No crash-recovery for the shared headless-browser singleton**: if the one Chromium process
  `HeadlessBrowserRenderProvider` keeps alive for the app's lifetime dies (OOM-killed, crashes), it
  stays dead — no disconnect detection or auto-relaunch exists yet. Every Pdf/Word export (formula
  rendering, and now Pdf printing) would fail until the whole app restarts. Flagged, not yet built.
- **Pdf export needs real fonts + fontconfig on the deploy target too, not yet confirmed present,
  and the failure mode is worse than the missing-library case above** (2026-07-17, found by direct
  reproduction — see `docs/deployment/overview.md`): a minimal host has no fontconfig/fonts by
  default. MathJax formulas still render fine (they're drawn as vector paths, not real fonts), but
  **all other text renders as nothing** — no error, no fallback font, just blank space — while
  borders/colors/images still render normally. The resulting Pdf looks like an empty, correctly
  laid-out template with no readable content, which reads as a data or template bug, not a missing-
  font one, unless you already know to suspect fonts. Minimum fix: install `fontconfig` +
  `fonts-liberation` and confirm `Arial`/`Helvetica`/`sans-serif` actually resolve to it (a bare
  fontconfig install with no alias rules can still pick an unrelated, e.g. monospace, font). Word is
  unaffected (rendered by the reader's own Word/LibreOffice, not this server).

None of the above block day-to-day feature work, but they should inform priorities and should not
be treated as "someone already fixed this."

## Recent notable changes

- Fixed `ImportLocations` migration batching (SQL Server error 701 on constrained instances).
- Full documentation system created (this file, `docs/`, `CLAUDE.md`, updated `README.md`/`CONTRIBUTING.md`) — 2026-07-10.
- **Resolved** the school "rate vs. rank" conflation: the schools list/details APIs now expose a
  genuine `Rating` field (0-5, `null` if no reviews) computed live from
  `AVG(SchoolComments.AverageRate)`, replacing the removed, mis-scaled `reviewScore` field.
  `Score`/`CountryRank`/`StateRank`/`CityRank` (internal ranking) are unchanged. See
  [`docs/business/school-scoring-analysis.md`](docs/business/school-scoring-analysis.md) — 2026-07-10.
- **Follow-up**: the internal ranking value (previously named `Score`) was renamed to `RankScore`
  (DB column + entity property, via migration `RenameScoreToRankScore`) since the shared "Score"
  word had become ambiguous next to `Rating`. It's no longer exposed via the public API at all (the
  school list previously still returned it as `score`). The `hasScore` list filter, which never
  actually checked `Score`/`RankScore` (it filtered by whether a school has reviews), was renamed to
  `hasRating`. See `docs/business/school-scoring-analysis.md` — 2026-07-10.
- **Follow-up 2**: the public rating field itself was renamed `Rate` → `Rating` (and `hasRate` →
  `hasRating`) — "rate" reads as a ratio/frequency (interest rate, conversion rate) in English,
  whereas "rating" is the standard term for a user-given star value (matches Google/Yelp/Amazon
  convention); no migration needed since it's computed live, not a DB column. — 2026-07-10.
- **Connections API fixes and CoreId-aware resolution** (2026-07-11 — see
  `docs/business/support-and-social.md`'s Connections section): `ConfirmFollowRequestAsync` no
  longer silently rejects a follow request it's supposed to confirm (was setting `Rejected` instead
  of `Confirmed`). All `users/{id}/...` connection endpoints accept an optional `idType` query
  param (`Id` default or `CoreId`, resolved via `IIdentityService.ResolveUserIdAsync`/
  `ResolveUserIdsAsync`) so a caller that only knows a legacy gama-api `CoreId` doesn't need a
  separate lookup first. New `POST connections/status` bulk-checks follow state for a list of
  users, for correct Follow/Following button UX.
- **Temporary legacy-auth bridge added** (2026-07-11 — see
  [`docs/api/authentication.md`](docs/api/authentication.md)'s "Legacy-auth bridge" section and
  [`docs/business/identity-and-access.md`](docs/business/identity-and-access.md)'s matching
  section): `LegacyAuthBridgeController` (`api/v1/legacy-auth`) proxies gama-api's
  login/register/recovery/googleAuth so the frontend can migrate off the old backend incrementally.
  `login`/`google` sync/link the local user (by `CoreId` → email → phone) and hand gama-api's own
  token back to the frontend **unchanged** — no gamatrain-back token is minted for this flow.
  `TokenAuthenticationHandler` now accepts that same gama-api JWT directly as an alternate
  `Authorization` credential (`ITokenService.VerifyLegacyTokenAsync`, resolved via `CoreId`), so the
  frontend holds exactly one token, identical to what it already gets from gama-api today, and
  gama-api needs zero code changes. Every code path accepting a gama-api JWT (this bridge, and the
  pre-existing `tokens/old`) now **cryptographically verifies its HS256 signature** against a new
  `Core:JwtSigningSecret` (real key, obtained from the gama-api team, not yet populated anywhere) —
  closing a real forgeable-token gap that existed in `tokens/old` before this change and that an
  earlier revision of this bridge would have inherited/widened. Trade-off: `tokens/revoke` (this
  backend's own store) can't touch a legacy-bridge session, since JWTs are stateless here — use
  the bridge's own `GET logout` instead (added 2026-07-13, see below) to end one early. Session
  lifetime is otherwise governed by gama-api's own token expiry, not this app's configurable token
  lifespan. `register`/`recovery` are pure passthroughs (gama-api never returns a token for those
  flows). Entirely temporary — this whole bridge, plus the pre-existing `tokens/old`, is meant to
  be deleted once the frontend fully migrates off gama-api.
- **Legacy-auth bridge logout added** (2026-07-13 — see
  [`docs/api/authentication.md`](docs/api/authentication.md)'s "Legacy-auth bridge" section):
  `GET legacy-auth/logout` proxies gama-api's own `GET /users/logout` (`Core:Logout` config,
  bearer-auth), relaying the caller's raw legacy JWT straight from the `Authorization` header. Pure
  passthrough like `register`/`recovery` — this backend never stored the token, so gama-api is the
  one actually invalidating the session; this is the one legacy-bridge operation that *does* end a
  session early, closing the gap called out in the entry above.
- **Content delivery & owner commissions added** (2026-07-13 — see
  [`docs/business/content-delivery.md`](docs/business/content-delivery.md)): new `POST downloads`
  resolves a download URL from one of gama-api's three legacy endpoints, selected by a new,
  dedicated `DownloadContentType` enum (exactly `PastPaper` → `/tests/download`, `Multimedia` →
  `/files/download`, `Exam` → `/exams/download`) — deliberately separate from the broader
  `ContentType` (which also has a `Test` member relevant only to the unrelated `games/spends`
  endpoint), so this feature's Swagger schema only ever advertises the 3 values it actually
  supports, via a new `IContentDeliveryProvider`/`ContentSource`-keyed provider, mirroring the
  payment-gateway provider pattern. Only `PastPaper` reports a price/owner — that charges the
  existing quota-then-points path only if gama-api hasn't already marked the download as paid, and,
  only if that charge succeeds, accrues a commission to the content's owner (resolved from
  gama-api's `CoreId`) in a new `ContentOwnerCommission` ledger, deliberately separate from both the
  points wallet and subscription quota. `Multimedia`/`Exam` report neither, so they're
  unconditionally free through this endpoint. Commission percent and a payout-eligibility threshold
  are admin-configurable via `ApplicationSettings`; the points-to-USD rate is a fixed first-phase
  constant (100 points = $1). Payout itself (crossing the threshold) is explicitly out of scope for
  this phase — no payout mechanism or paid-status column exists yet (Stripe is the intended rail
  per 2026-07-14 direction, likely alongside other methods, but not built).
- **Content-owner commission report added** (2026-07-14 — see
  [`docs/business/content-delivery.md`](docs/business/content-delivery.md)'s "Commission report"
  section): two read-only list endpoints over the `ContentOwnerCommission` ledger above, on a
  dedicated `CommissionsController` (deliberately not nested under `DownloadsController` — a
  commission's `Reason` is meant to outlive "download" as the only event that earns one) —
  `GET commissions` (`User`, forced to the caller's own rows via `OwnerUserIdEqualsSpecification`,
  no `ownerUserId` field exists on this endpoint's request model at all) and `GET admin/commissions`
  (`Admin`, any/all owners, optional `ownerUserId` filter). Both share
  `IContentDeliveryService.GetContentOwnerCommissionsAsync` and
  `ContentOwnerCommissionListResponseViewModel`; filterable by `startDate`/`endDate`. Still no
  paid/payout state — this is reporting only, ahead of the payout phase noted above.
  `CommissionReason.LegacyContentDownload` was also renamed to `ContentDownload` same day — the
  "Legacy" prefix mislabeled intent, since gama-api is meant to stay as one of potentially several
  permanent content sources, not be retired like the temporary legacy-auth bridge.
- **Quota-based subscription system built** (2026-07-10, phase 1 — see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)): `SubscriptionPlan` no
  longer carries a price — pricing moved to `SubscriptionPlanPrice` (regional-pricing-ready,
  gated dormant behind `Subscription:RegionalPricingEnabled`, default `false`) and quotas moved to
  a new `Feature`/`SubscriptionPlanFeature` catalog. Purchasing a plan reuses the existing
  Payment/gateway checkout flow (never the currency→points conversion); `games/spends` now tries
  subscription quota before falling back to wallet points, unchanged for non-subscribers.
  Deliberately deferred: PayPal, native recurring billing, a real FX source for base-currency
  reporting, and in-house pastpaper file serving.
- **Inbound ticket emails no longer degraded to mangled plain text** (2026-07-14 — see
  [`docs/business/support-and-social.md`](docs/business/support-and-social.md)):
  `ResendEmailProvider.ProccessInboundEmailAsync` was reading the received email's `TextBody` —
  Resend's auto-generated plain-text fallback for an HTML email, which flattens `<img>`/`<a>` tags
  to `[url]text` — instead of `HtmlBody`, the actual message. Every inbound HTML email (the normal
  case for anyone using a real email client) arrived in the ticket system already mangled. Now takes
  `HtmlBody`, falling back to `TextBody` only when the sender's email genuinely had no HTML part.
- **Legacy-auth bridge forwards the real client IP to gama-api** (2026-07-17 — see
  [`docs/api/authentication.md`](docs/api/authentication.md)'s "Legacy-auth bridge" section):
  `login`/`google`/`register`/`recovery` were proxied straight through, so gama-api's own
  rate-limiting/fraud checks only ever saw this server's IP, never the actual end user's.
  `IdentityService` now reads the caller's IP off the inbound request and `CoreProvider` sends it as
  a `TRUSTED_FORWARDED_IP` header on those four outgoing calls (`logout` unaffected — gama-api didn't
  ask for it there).
- **Word and PowerPoint exam export rewritten as fully native OOXML; Spire and HtmlToOpenXml both
  fully removed from the solution** (2026-07-16 to 2026-07-17 — see
  [`docs/business/exams-and-content.md`](docs/business/exams-and-content.md)): the `Word` branch of
  `ExamSerivce.ExportExamAsync` builds a `.docx` by hand-emitting `DocumentFormat.OpenXml` elements
  directly (`ExamWordDocumentBuilder.cs`/`ExamWordRichText.cs`) — no HTML-to-OOXML conversion layer
  at all, after HtmlToOpenXml.dll (an earlier intermediate step) proved unable to produce genuinely
  native-quality Word tables (silently applied its own default `TableGrid` style, mishandled
  bare-pixel widths). The `PowerPoint` branch (`ExamPresentationBuilder.cs`) got the same treatment,
  replacing paid Spire.Presentation's `AddFromHtml` — one slide per question after a title/summary
  slide, PresentationML's own `ThemePart`/`SlideMasterPart`/`SlideLayoutPart` hierarchy built from
  scratch, absolutely-positioned shapes instead of flowing tables, options grid as a native DrawingML
  table. Known PowerPoint gap: slides use plain-text runs only (`BuildRichParagraphs`) — no bold/
  italic/color formatting (formulas are handled, see below). Both `Spire.Officefor.NETStandard` and
  `HtmlToOpenXml.dll` package references are
  gone from every `.csproj` and `Directory.Packages.props`. Two OOXML schema traps worth remembering:
  every `w:tbl` needs an explicit `w:tblGrid` right after `w:tblPr` (its absence makes Word silently
  repair/collapse the table on open) and a table cell's content must end with a paragraph, not a
  table (a cell whose last child is a nested `w:tbl` renders as if it broke out of the cell).
  Question/option text can contain MathJax-style `$...$` LaTeX (confirmed from real Core exam data,
  including non-trivial `\begin{gathered}...\end{gathered}` constructs) — a singleton headless-browser
  provider renders these to PNGs using the real MathJax engine inside a headless Chromium tab
  (PuppeteerSharp), since partial-LaTeX .NET parsers failed on the messier real-world formulas; at
  this point Word embedded the resulting PNGs natively and PowerPoint didn't call formula rendering
  at all — both superseded by native `m:oMath` for Word/PowerPoint, see the OMML bullet below.
  Concurrent renders are capped at `Environment.ProcessorCount` via a semaphore so a
  burst of simultaneous export requests queues instead of overwhelming the shared browser process.
  Also fixed in passing: `CoreExamInformationResponse.RemainedSeconds` was typed `bool` but Core
  actually returns a signed integer (broke deserialization for any exam); the QR code data URI had
  an invalid MIME type (`img/png` instead of `image/png`); embedded images were being encoded at
  full source resolution regardless of declared display size, needlessly bloating every export.
- **Pdf exam export rewritten off Spire, reusing the Word pipeline's Chromium instance**
  (2026-07-17): `IMathFormulaRenderProvider`/`MathJaxFormulaRenderProvider` renamed to
  `IHeadlessBrowserRenderProvider`/`HeadlessBrowserRenderProvider` to reflect its now-broader
  responsibility, and gained `RenderPdfAsync` — prints formula-rendered HTML to PDF via Chromium's
  own native print engine (`PrintBackground: true`, A4, 0.5in left/right margins), reusing the same
  singleton browser/concurrency-limiter rather than adding a second Chromium instance or a separate
  PDF library. Pdf is deliberately the one format that still renders from real HTML (via the
  `exam.word.html` Handlebars template, name predates the Word rewrite) instead of native OOXML —
  PDF is painted pixels, not an editable document, so the "HTML can't produce a genuinely native
  table" problem that motivated the Word/PowerPoint rewrites doesn't apply to it. Watermark for Pdf
  is a `position:fixed` (deliberately, not `absolute` — Chromium's print engine repeats fixed-position
  elements on every printed page) diagonal `<div>` injected before printing, HTML-encoded. See the
  deployment risk noted above — Chromium native libraries are required for Pdf exports (and Word/
  PowerPoint's MathJax formula rendering) to work at all.
- **Word/PowerPoint formulas switched from rasterized PNG to native OOXML Math (`m:oMath`)**
  (2026-07-18, see [`docs/business/exams-and-content.md`](docs/business/exams-and-content.md)):
  motivated by Word/PowerPoint's actual audience being teachers who edit/reuse the export (unlike
  Pdf, read by students) — a raster formula can't be edited, and PowerPoint previously dropped
  formulas entirely (the known gap above). MathJax's existing `tex-svg.js` already emits a hidden
  MathML annotation by default (`assistiveMml:!0`) alongside the SVG it renders, so no separate
  MathJax bundle/render pass was needed; that MathML is converted to OOXML Math via a newly-vendored
  `wwwroot/lib/mathml2omml/mathml2omml.js` (npm `mathml2omml` 0.5.0, LGPL-3.0-or-later, a
  from-scratch reimplementation — deliberately not Microsoft's own `MML2OMML.xsl`, which isn't
  safely redistributable), running in the same headless Chromium page as MathJax, so no new .NET
  dependency. Two real bugs found and patched in the vendored copy by validating against
  `DocumentFormat.OpenXml`'s `OpenXmlValidator` (not just "is this well-formed XML," a materially
  weaker check that missed both): (1) the library's `stringify()` wrote text node content with zero
  XML escaping, producing invalid XML for any formula whose text contained a literal `<`/`&`; (2)
  `addScriptlevel()` added a duplicate, schema-invalid `<m:argPr><m:scrLvl>` for every invisible-
  spacing `mstyle` MathJax emits inside `\begin{gathered}` piecewise constructs. Word inserts
  `m:oMath` as a direct sibling of `w:r` runs, inline with text, same as Word's own equation editor.
  PowerPoint has no such direct slot in DrawingML's `a:p` schema — equations there require the
  `mc:AlternateContent`/`a14:m` markup-compatibility wrapper (PowerPoint 2010+), and each formula
  becomes its own dedicated paragraph rather than staying inline mid-sentence, since AlternateContent
  isn't valid mixed into one paragraph alongside plain runs. Both paths fall back to the previous
  rendered-PNG `<img>` per formula if the MathML→OMML conversion throws. Pdf is unchanged (still
  images, via `RenderFormulasAsync`) since its HTML+Chromium-print pipeline has no OOXML to insert
  native math into anyway.
- **Native recurring billing added for Stripe** (2026-08-10, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Native recurring billing
  (Stripe)" section): a Stripe subscription purchase now auto-renews by default via a real Stripe
  Subscription (Checkout `Mode = "subscription"`) instead of staying a one-time charge —
  `SubscriptionPlanGatewayMapping` (built 2026-07-10, unread until now) is finally consumed. A new
  `IRecurringPaymentGatewayProvider` capability (gateway-parameterized, only Stripe implements it —
  GamaTrain's crypto wallet has no saved-payment-method concept and never will) backs a new
  `[AllowAnonymous]` `POST payments/webhooks/{gateway}` receiving Stripe's `invoice.paid`/
  `customer.subscription.deleted` events, signature-verified against a new
  `PaymentGateway:Stripe:WebhookSecret` secret. Renewal extends the *same* `UserSubscription` row
  (no new row per period) and resets its quota; idempotency against webhook redelivery reuses
  `Payment`'s existing `(TransactionId, Gateway)` unique index. Dunning relies entirely on Stripe's
  own Smart Retries — no hand-rolled retry logic. This phase only reacted to the gateway's own
  end-of-subscription event — user-initiated cancellation was a separate follow-up (below).
- **User-facing subscription cancellation added** (2026-08-11, issue #536, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "User-facing subscription
  cancellation" section): `POST subscriptions/me/cancel` / `POST subscriptions/me/resume` —
  cancel-at-period-end (not immediate; no refund needed, quota stays usable until `ExpirationDate`).
  Two new `UserSubscription` columns, `ExternalSubscriptionId` (the gateway's own recurring-subscription
  id, captured at activation time — closes the earlier gap where nothing stored it) and
  `CancelAtPeriodEnd`, both now also exposed as `autoRenews`/`cancelAtPeriodEnd` on `GET
  subscriptions/me`. Both actions send a confirmation email (two new `ApplicationSettingsDto`
  templates, admin-editable) via the same Hangfire `BackgroundJob.Enqueue` pattern used everywhere
  else in this codebase — enqueued from `SubscriptionsController`, not `SubscriptionService`, keeping
  Hangfire out of the Application layer.
- **Admin visibility/management of user subscriptions added** (2026-08-12, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Admin visibility/management
  of user subscriptions" section): before this, the admin `SubscriptionsController` only managed the
  catalog (plans/features/prices/gateway-mappings) — zero way to look up a user's subscription(s) or
  act on one for a support case. New `api/v1/admin/subscriptions/users` endpoints: list/detail
  (read-only, exposes `externalSubscriptionId`/`gateway` unlike the self-service response), a comped
  `grant` (creates+activates a subscription immediately, bypassing payment), an immediate `revoke`
  (distinct from the user-facing cancel-at-period-end flow — terminates the gateway-side subscription
  first via a new `IRecurringPaymentGatewayProvider.TerminateSubscriptionAsync`, Stripe:
  `SubscriptionService().CancelAsync`), and `extend` (pushes `ExpirationDate` forward, local-only).
- **Plan upgrade/downgrade with proration added** (2026-08-12, issue #554, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Plan upgrade/downgrade with
  proration" section): `POST subscriptions/me/switch` (Stripe-recurring only). Backend decides
  upgrade-vs-downgrade by price comparison — an upgrade applies immediately (Stripe invoices the
  prorated difference now); a downgrade is deferred to the current period's end via a Stripe
  **Subscription Schedule** (a bare `ProrationBehavior=none` update does not defer *when* a price
  change applies, only whether a proration invoice line is generated — verified against Stripe's
  docs). Two new `UserSubscription` columns, `PendingSwitchSubscriptionPlanId`/
  `PendingSwitchPricePaid`, applied by `RenewSubscriptionAsync` at the next renewal boundary.
  `CancelSubscriptionAsync`/`ResumeSubscriptionAsync`/`TerminateSubscriptionAsync` were retrofitted
  to be schedule-aware; live verification against real Stripe test-mode objects caught and fixed a
  real bug where an early version of `ResumeSubscriptionAsync` unconditionally released any attached
  schedule, silently destroying a legitimately-pending downgrade. A pending downgrade is exposed on
  `GET subscriptions/me` and the admin equivalent as `pendingSwitchPlanId`/`pendingSwitchPlanTitle`
  (added right after initial ship, once frontend work surfaced needing it for a status badge).
- **Self-service subscription history added** (2026-08-13, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Self-service subscription
  history" section): `GET subscriptions/me/history` (paged, newest first) — the caller's own past
  `UserSubscription` rows, `Status` Expired or Cancelled only (`Pending`/`Active` excluded, the
  latter already being `GET subscriptions/me`'s job). No new entity/migration — reuses the existing
  `UserIdEqualsSpecification`/`UserSubscriptionStatusEqualsSpecification` composition already used
  by the admin listing, projected into a new self-service-only `UserSubscriptionHistoryDto` (no
  `UserId`/`UserEmail`/`ExternalSubscriptionId`/`Gateway`, same admin-only fields excluded from
  `subscriptions/me`).
- **Consumption history & admin usage reporting added** (2026-08-13, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Consumption history & admin
  usage reporting" section): new `SubscriptionQuotaConsumptionLog` table, one row per successful
  `ConsumeQuotaAsync` call (deliberately no FK to `UserSubscriptionQuota`, since plan switches
  delete/re-snapshot that table's rows and would otherwise wipe history). Log write is best-effort,
  isolated in its own `try`/`catch` so a logging failure can never turn an already-committed quota
  decrement into a reported failure. Two new admin endpoints: `GET admin/subscriptions/usage` (raw
  event log, filterable) and `GET admin/subscriptions/usage/aggregate` (per-feature totals for a date
  range, per-user or global depending on whether `userId` is supplied). Self-service surfaces
  (`subscriptions/me`, `subscriptions/me/history`) are unaffected — this is admin-only reporting.
- **Resolved gap: subscription quota now scales with billing interval** (2026-08-13, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s `SubscriptionPlanFeature`
  section): previously `SubscriptionPlanFeature.Limit` was plan-wide only — buying the Annual
  variant of a plan granted the exact same per-feature limit as Monthly, just for a longer period,
  under-rewarding longer commitments. `SubscriptionPlanFeature` now carries a `BillingInterval`
  column (unique on `SubscriptionPlanId, FeatureId, BillingInterval`), so an admin can set a
  different explicit limit per interval a plan is sold at — no automatic multiplier, and still never
  keyed by `Price`/`Currency` (that invariant is unchanged, see CLAUDE.md). Ripples through
  `SubscriptionQuotaService.CreateQuotasAsync` (now resolves the snapshot at the subscription's own
  interval) and the upgrade-suggestion response (`Limit`/`Description`/`PooledFeatureCodes`/
  `FeatureGroups` moved from the top-level suggestion down into each `Prices` entry, since a plan's
  quota can now differ per interval) — a public response shape change for
  `games/spends`/downloads' `upgradeSuggestions` payload, plus the admin `plans/{id}/features`
  request/response shape (`limit` → `limits: [{ billingInterval, limit }]`). Migration
  `AddBillingIntervalToSubscriptionPlanFeature` backfills every existing row across each plan's
  currently-sold intervals with its existing flat limit, so there's no behavior change until an
  admin edits per-interval numbers going forward.
- **Follow-up**: `GET subscriptions/me` now also surfaces each quota bucket's `planLimits: [{
  billingInterval, limit }]` — the current plan's own limit at every interval it's sold at, fetched
  live (not snapshotted), alongside the subscriber's own `limit`/`used`/`remaining`. Lets a client
  show "you're on Monthly: 50, this plan's Annual: 600" directly on the subscription screen.
  Also added an optional `subscriptionPlanId` filter to `GET admin/subscriptions/prices`, wiring up
  a `PlanIdEqualsSpecification` that already existed but was unused.
- **Fixed bug: content downloads consumed a flat 1 unit of subscription quota regardless of the
  file's price** (2026-08-14, see
  [`docs/business/content-delivery.md`](docs/business/content-delivery.md)'s "Charge:
  quota-then-points" section and [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s
  "Quota consumption and the points fallback" section). `GameService.SpendPointsAsync` hardcoded
  `Amount = 1` on every `ConsumeQuotaAsync` call, so a subscriber's monthly download allowance
  drained by exactly 1 per download no matter whether the file cost 1 point or 500.
  `SpendPointsRequestDto` now carries a separate `QuotaAmount` (default `1`) alongside `Points`;
  `ContentDeliveryService` sets it to the same gama-api-reported price already used for the wallet
  fallback. `games/spends` deliberately keeps the flat-1 behavior (its `Points` is client-supplied,
  never verified against gama-api, so trusting it for quota too would let a caller drain a feature's
  whole allowance in one call). Not a reintroduction of "quota never derived from payment amount" —
  that rule covers the plan's own `Limit` vs. the subscription's paid price; this is how much of that
  fixed limit one action draws down, scaled by the *content's* price (see CLAUDE.md).
- **Added: dunning visibility via `invoice.payment_failed`** (2026-08-14, see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Dunning visibility"
  section). Found while auditing which subscription lifecycle actions (cancel, resume, admin revoke,
  upgrade, downgrade) actually depend on a webhook to complete — cancel and downgrade both correctly
  do (by design); the one genuine gap was `invoice.payment_failed`, which wasn't recognized at all
  (not even an enum member), so a failed renewal charge was completely invisible locally for the
  entire length of Stripe's retry window (can run for weeks). Fixed as visibility-only, not an
  access-control change: new `UserSubscription.LastPaymentFailedDate` column (migration
  `AddLastPaymentFailedDateToUserSubscription`), stamped by a new `RecurringWebhookEventType.
  PaymentFailed` → `PaymentService.HandlePaymentFailedAsync`, cleared back to `null` on the next
  successful `RenewSubscriptionAsync`. Exposed on `GET subscriptions/me` and the admin subscription
  endpoints. `Status`/`ExpirationDate`/quota are completely unaffected — "Dunning is entirely
  Stripe's" (no hand-rolled retry/grace-period logic) is unchanged. Verified live against a local SQL
  Server and the real running API using a Stripe.net-signed synthetic webhook event (no real Stripe
  account involved). Also documented a previously-dangling "Trial periods backlog item" code-comment
  reference (still out of scope, just now actually written down in the "Deliberately out of scope"
  list).
- **Fixed bug: a user could end up with two simultaneously Active, independently-billed
  subscriptions** (2026-08-15, found live in production - a user with both Alpha and Beta
  Active at once, both real, auto-renewing Stripe subscriptions each charging the card on
  its own schedule). See
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Purchase → verify →
  activate lifecycle" and "Quota consumption and the points fallback" sections.
  `PurchaseSubscriptionAsync` now rejects (`OperationResult.Duplicate`) if the caller
  already has an Active subscription - a server-side backstop, not just a frontend
  convention, since nothing previously stopped a second purchase while one was already
  Active. (Superseded 2026-08-16, same PR #575 - see the "merged purchase/switch" entry
  below: this outright rejection was the first cut, later changed to delegate to a switch
  instead of just rejecting.) Root cause: the quota-exhausted/insufficient-balance response
  (`ConsumeQuotaResponseDto`/`SpendPointsResponseDto`/`DownloadContentResponseDto` and their
  ViewModels) gave a client acting on `UpgradeSuggestions` no way to tell "I already have a
  subscription, route this as a switch" from "I have nothing, this should be a fresh
  purchase" - especially risky since the upgrade-suggestion card is deliberately
  schema-compatible with the general "subscribe to this plan" card, inviting shared-component
  reuse. Fixed by adding `Reason`/`CurrentSubscriptionId`/`CurrentPlanId`/`CurrentPlanTitle`
  to that response, threaded through `GameService.SpendPointsAsync`,
  `ContentDeliveryService`'s two download paths, `POST v2/games/spends`, and
  `POST downloads`. Verified live: the purchase guard rejects with zero side effects (no
  duplicate row created), and the new response fields correctly resolve for both the
  `NoActiveSubscription` and `QuotaExhausted` cases against a real local SQL Server.
- **Fixed bug: a genuine duplicate/concurrent plan-switch request could double-charge a card**
  (2026-08-16, same PR #575 as the item above - found while reasoning through the upgrade billing
  math with the user). `SwitchSubscriptionPlanAsync`'s immediate-upgrade path bills synchronously
  (`ProrationBehavior = "always_invoice"`), but `StripePaymentGatewayProvider.RequestOptions` mints a
  fresh idempotency key on every access, so Stripe had no way to recognize a double-click/retry as the
  same operation - and the gateway call happened before any local write, so even a correct DB-level
  check couldn't have prevented the second real charge. Fixed with a new
  `UserSubscription.SwitchLockedUntil` column, claimed via a guarded conditional `UPDATE` *before* the
  gateway call, so a concurrent duplicate request is rejected locally and never reaches Stripe at all.
  See [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s "Plan upgrade/downgrade
  with proration" and `CLAUDE.md`'s new sharp edge - the same underlying weak-idempotency-key pattern
  exists on this provider's other Stripe-mutating calls (cancel/resume/terminate) and hasn't been
  individually audited yet. Verified live: a claim taken while a lock is already held is rejected with
  zero gateway calls made.
- **Added: `subscriptions/me/switch` can now move billing interval, not just plan - upgrade direction
  only** (2026-08-16, same PR #575). Previously a user on Alpha-Monthly wanting Alpha-Annual had no
  supported path at all - `switch` rejected same-plan requests outright regardless of interval, and
  (after the duplicate-active-subscriptions fix above) `purchase` correctly rejects it too since
  they're already Active. Worth fixing because per-interval quota limits (2026-08-13) mean a bigger
  interval can grant meaningfully more quota, not just a different price - a real quota upgrade, the
  same category `switch` already exists to handle for plan tiers. `billingInterval` is now an optional
  field on the switch request; the existing immediate/deferred price-comparison rule is reused
  unchanged (a bigger interval's price is always numerically greater, so it already classifies
  correctly as immediate); a move to a *smaller* interval is rejected outright
  (`IntervalDowngradeNotSupported`) rather than silently mishandled, since the deferred path has no
  field to carry an interval change through to renewal and unused paid-for time raises a refund-policy
  question out of scope for this fix. See
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md), "Switching billing interval (not
  just plan), for a bigger interval only". Verified live against a real local SQL Server + running API
  without calling real Stripe: same-plan-same-interval and same-plan-smaller-interval both correctly
  rejected; same-plan-bigger-interval correctly passes every guard through to the `SwitchLockedUntil`
  claim.
- **Changed: `plans/{id}/purchase` now delegates to a switch instead of just rejecting, with a
  preview-then-confirm step for upgrades that charge immediately** (2026-08-16, issue #576, PR
  #577, requested directly: "why do we need an extra endpoint for buy and upgrade/downgrade -
  can purchase handle switch under the hood?"). (Pushed to the same branch as #575 after #575
  had already merged, so it shipped as a separate PR rather than landing in #575 itself.) The 2026-08-15 fix above stopped the double-billing
  bug but pushed the burden onto the client - it had to check `CurrentSubscriptionId` and
  branch between `purchase` and `me/switch` itself. Now `purchase` detects an existing Active
  subscription and calls the same switch logic internally, so one "buy this plan" button works
  whether the caller is new or already subscribed; all of `me/switch`'s own rejections
  (`SubscriptionNotRecurring`, `SamePlanSwitchNotAllowed`, `IntervalDowngradeNotSupported`) are
  reachable through `purchase` too, unchanged. Because an immediate upgrade bills the card
  synchronously, both endpoints gained a `Confirm` flag (default `false`): an upgrade attempt
  without it returns a no-op preview (`requiresConfirmation: true`, `previewAmount`,
  `previewCurrency`, via a new `IRecurringPaymentGatewayProvider.
  PreviewSwitchSubscriptionPlanAsync` wrapping Stripe's own `InvoiceService.
  CreatePreviewAsync` - a real proration calculation with zero side effects, no
  `SwitchLockedUntil` claim taken); resubmitting identically with `Confirm: true` applies it and
  charges. `me/switch` is kept as a separate endpoint, deliberately - better fit for a dedicated
  manage-subscription screen; `purchase` is now the recommended single entry point for a
  generic buy/upgrade UI. See [`docs/business/subscriptions.md`](docs/business/subscriptions.md),
  "Purchase now also performs switches, with a confirm step for real charges", and
  [`docs/api/endpoints.md`](docs/api/endpoints.md)'s `SubscriptionsController` section. Verified
  live against a real local SQL Server + running API without calling real Stripe: delegation
  routing confirmed correct for a non-recurring existing subscription
  (`SubscriptionNotRecurring`), an identical plan+interval (`SamePlanSwitchNotAllowed`), and a
  smaller-interval request (`IntervalDowngradeNotSupported`), each with the response correctly
  carrying the *existing* subscription's id and no stray rows created.
- **Added: `IsCurrent`/`CanUpgrade` flags on every `UpgradeSuggestions` price entry, and the
  list is no longer filtered or capped** (2026-08-16, requested directly: "I need a flag so
  frontend can detect upgrade to these plans impossible"). Before this, a (plan, interval) pair
  only appeared in the quota-exhausted response (`v2/games/spends`, `POST downloads`) if its
  `Limit` genuinely beat the caller's current one - up to the 3 cheapest qualifying prices per
  interval; the caller's own current plan+interval was never included, and neither was any
  plan/interval offering equal-or-less quota. A client wanting to render a fixed, complete plan
  grid (every plan × every interval, non-upgradeable ones greyed out) had no way to do that from
  this response alone. Now every (plan, interval) pair offering the failed feature on an active
  plan is always returned, each flagged: `IsCurrent` (the exact plan+interval the caller is
  already on - compared by id, not by limit value, so a live admin limit change can't make
  "switching" to the identical subscription look selectable) and `CanUpgrade` (`false` for
  `IsCurrent` and for anything that doesn't actually exceed the caller's current limit, `true`
  otherwise). Scoped deliberately to just this response, not the general `GET subscriptions/plans`
  catalog - see [`docs/business/subscriptions.md`](docs/business/subscriptions.md), "Quota
  consumption and the points fallback." Verified live against a real local SQL Server + running
  API: a subscription active on Alpha/Monthly with `PastpaperDownload` exhausted returned every
  plan offering that feature at every interval each is actually sold at; Alpha/Monthly itself
  came back `isCurrent:true, canUpgrade:false`; Alpha's other intervals and a same-limit plan
  (Pro) came back `canUpgrade:false` without being current; a lower-limit plan (GamaTest) also
  came back `canUpgrade:false`; every higher-limit plan/interval came back `canUpgrade:true`.
- **Fixed bug: an immediate plan-switch charge never showed up in the admin `payments` report**
  (2026-08-16, found live in the sandbox: buy a plan, upgrade it, the upgrade's proration charge
  is missing from the report even though Stripe genuinely charged the card). Root cause:
  `StripePaymentGatewayProvider.ParseWebhookEventAsync`'s `invoice.paid` match only recognized
  `BillingReason == "subscription_cycle"` (an ordinary renewal); Stripe's proration invoice for
  an immediate switch carries `BillingReason == "subscription_update"` instead, a third case the
  original two-way `subscription_create`/`subscription_cycle` split never accounted for -
  unmatched, it silently fell to `Ignored`, so no `Payment` row was ever created for it. Fixed
  with a new `RecurringWebhookEventType.PlanChangeInvoicePaid`, recorded using the invoice's own
  `AmountPaid` (never `UserSubscription.PricePaid`, which by webhook-arrival time has already
  been overwritten to the new plan's full price by `ApplyPlanSwitchAsync` - would have recorded
  the wrong amount), and deliberately never calling `RenewSubscriptionAsync` (a plan-change
  invoice isn't a new billing period - Renew would incorrectly extend `ExpirationDate` and reset
  quota `Used` to 0 as a side effect of a mid-cycle upgrade). See
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md), "Immediate plan-switch
  charges weren't recorded as Payments." Verified live using a Stripe.net-signed synthetic
  webhook event (no real Stripe account involved): a `Payment` row was correctly recorded with
  the invoice's own $4 amount, `ExpirationDate`/quota `Used` both confirmed unchanged, and
  redelivering the identical event produced no second row.
- **Added: `GET admin/subscriptions/users/{id}` now returns `featureGroups` - live quota status
  (`Limit`/`Used`/`Remaining` per feature group)** (2026-08-17, found live: a support case needed
  to know whether a real customer could still use their remaining subscription after an admin
  `revoke` on a duplicate one, and no admin endpoint anywhere exposed the actual live quota state -
  the closest existing tool, `usage/aggregate`, gives consumption totals but never the plan's own
  `Limit` to compare against). New `SubscriptionQuotaStatusDto`/`ViewModel`, populated only on the
  single-subscription detail call (not the paged list, to avoid an extra query per row on every
  page). See [`docs/business/subscriptions.md`](docs/business/subscriptions.md), "Admin
  visibility/management of user subscriptions." Also documented a real sharp edge found while
  debugging a live customer case: `{id}` on every `admin/subscriptions/users/{id}/...` route is the
  `UserSubscriptionId`, not the `UserId`, despite the `users/` path segment - easy to get wrong
  when the admin UI's own list can show the same `UserId` twice (one user, two subscriptions) with
  no visible subscription id in the table. Verified live against a local SQL Server + running API:
  a capped bucket (`Limit: 300, Used: 45`) correctly returned `Remaining: 255`; an unlimited bucket
  (`Limit: null, Used: 5`) correctly returned `Remaining: null` rather than erroring; the paged list
  confirmed to leave `featureGroups` unset.
- **Fixed bug: `subscriptions/me/switch` rejected every interval downgrade outright**
  (2026-08-19, live-reported: "its incorrect behavior in this endpoint allow user downgrade"). The
  2026-08-16 interval-switch work only supported moving to a *bigger* interval; a smaller one always
  hit `IntervalDowngradeNotSupported`, even for a plain plan-tier downgrade that happened to also
  request a smaller interval - the endpoint is supposed to allow downgrades. Fixed by giving the
  deferred/schedule downgrade path a new nullable `UserSubscription.PendingSwitchBillingInterval`
  column (migration `AddPendingSwitchBillingIntervalToUserSubscription`), so an interval downgrade
  now defers to the current period's end exactly like a plan-only downgrade already did - the
  refund/credit question the original rejection worried about never actually arises, since nothing
  is billed differently until the deferred switch applies at the next renewal. No gateway-side
  change was needed: Stripe's own deferred-switch mechanism (a 2-phase Subscription Schedule) was
  already keyed only by the new Price id, interval-agnostic from the start. Also exposed as
  `pendingSwitchBillingInterval` on `GET subscriptions/me`/`GET admin/subscriptions/users(/{id})`,
  alongside the existing `pendingSwitchPlanId`/`pendingSwitchPlanTitle`. See
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md), "Interval downgrade now
  supported, deferred to period end."
- **Renamed `BillingInterval.Seasonally` → `Quarterly` and `BillingInterval.Yearly` → `Annual`**
  (2026-08-19) to match conventional billing terminology (`Daily`/`Weekly`/`Monthly` unchanged).
  Pure symbol rename — underlying `Value`/`Days` unchanged, so existing purchased subscriptions
  need no data migration — but it **is** a breaking JSON wire-contract change (the enum
  serializes as its `Name` string), so the frontend/mobile clients must be updated in the same
  release. See [`docs/business/subscriptions.md`](docs/business/subscriptions.md)'s `Entities`
  section for detail.
- **Fixed security gap: the Hangfire dashboard (`/hangfire`) was fully public with no authentication**
  (2026-08-22, found live). `app.UseHangfireDashboard()` ran with no `DashboardOptions` at all, so it fell
  back to Hangfire's own default, `LocalRequestsOnlyAuthorizationFilter` - meaningless behind this app's
  reverse-proxy topology (nginx forwarding to `http://127.0.0.1:5000`), since every request Kestrel sees
  arrives from `127.0.0.1` regardless of the real external client. Confirmed live on both production and
  the sandbox: full job history plus the ability to trigger/requeue any registered job, open to anyone who
  found the URL. Fixed with a new `HangfireDashboardAuthorizationFilter`
  (`IDashboardAsyncAuthorizationFilter`) requiring the Identity cookie scheme + `Admin` role. This was
  already flagged as a hardening item in `docs/architecture/cross-cutting-concerns.md` before this fix -
  see that file's "Background jobs (Hangfire)" section for the corrected detail.
- **Also found while fixing the above**: separately, `ConvertAvatarsAsync` (the one-time backfill
  converting legacy base64 `ApplicationUser.Avatar` values to real files) had its whole loop in a single
  try/catch - one corrupt legacy row aborted the entire batch silently. Fixed with per-user isolation, real
  counts (`Converted`/`Skipped`/`Failed`), and a new admin-only trigger, `POST
  admin/identities/convert-avatars`, enqueuing a Hangfire background job rather than running inline -
  checked against real production data live (22,451 of 28,914 users still on the legacy column), running
  that inline would take 7-11+ minutes, well past any realistic HTTP timeout. See PR #591/#594.
- **Found live while chasing the above: the `wwwroot/Files/user` permission bug kept reverting on every
  deploy** (2026-08-22). Root cause: 4 files had been accidentally committed to git under
  `wwwroot/Files/user/` (stray local-testing artifacts from the original avatar-file-provider work). Every
  `dotnet publish` on the GitHub Actions runner picked them up, owned there by the runner's own default
  account - Ubuntu GitHub-hosted runners use UID **1001** for this (not 1000), and `scp-action` carried
  that ownership through unchanged to the VPS, recreating the directory with a UID that maps to no local
  account, blocking `www-data` (the app's own service user) from writing new uploads. Fixed by removing
  the 4 files from git and adding `/src/Presentation/Api/wwwroot/Files/` to `.gitignore` - it's purely a
  runtime upload target, was never meant to carry tracked content. See PR #598.
- **Avatar migration completed, legacy column removed** (2026-08-22). Once the background-job backfill
  (above) ran against production: 22,451 of 28,914 users converted; the only remaining 322 rows with a
  non-null `Avatar` all already had `AvatarId` set too (stale leftovers from a normal avatar update that
  happened before the backfill ran and never cleared the old column - `ManageAvatarAsync` only ever sets
  `AvatarId`, it doesn't touch `Avatar`) - confirmed live, zero rows were left with `Avatar` set and
  `AvatarId` still null. With every real avatar now safely represented by `AvatarId`, removed the whole
  one-time-backfill machinery (`ConvertAvatarsAsync`, its DTO, and the admin endpoint - job's done, no
  longer needed) and the `Avatar` column itself via a real `dotnet ef migrations add`
  (`RemoveLegacyAvatarColumn`) rather than hand-authoring the migration files.
- **New `GET identities/dashboard` proxy, Phase 0** (2026-09-01 — see
  [`docs/business/identity-and-access.md`](docs/business/identity-and-access.md)'s "User dashboard
  proxy" section): gives gamatrain-front's user dashboard page one merged payload from this
  backend, replacing its previous direct calls to gama-api's `GET /teachers/dashboard` / `GET
  /students/dashboard`. Phase 0 is a deliberately staged first step - a field-for-field passthrough
  of gama-api's response only, nothing new added yet. The server now picks teacher vs. student
  itself from the caller's local `ApplicationUser.Group` (previously a client-side choice), and a
  failed/unreachable legacy call degrades to `DashboardResponseDto.LegacyDataAvailable = false`
  (every other field null) rather than failing the whole request. Two dashboard widgets -
  subscription banner and achievements/badges - are deliberately untouched by this phase: real
  subscription data is planned as a later, separate step (the domain already exists in this
  backend); badges/achievements has no domain in *either* backend and is out of scope for this
  proxy entirely, treated as its own future feature rather than bundled in.
- **`identities/dashboard` Phase 2: mostly local data now, same day** (2026-09-01, immediately after
  Phase 0 above — see [`docs/business/identity-and-access.md`](docs/business/identity-and-access.md)'s
  "User dashboard proxy" section for the full field-by-field table): `user`/`profileCompletion`/
  `unreadMessages` are now built entirely from this backend's own data - always populated,
  independent of gama-api - using `CoreId`/`Role` names/`AvatarUri`/`Handle`/`Gender`/`CurrentBalance`
  (as `points`)/City·School titles/`Board`·`Grade`/a repackaged `UserRateLevel`-based
  profile-completion score/the local `Message` entity's unread count. The subscription banner now
  has real data too (`user.subscription`, via `ISubscriptionQuotaService`) - `null` on the free
  tier. `Board`/`Grade` replaced gama-api's `section`/`course` (same curriculum-board/grade-level
  concept, different scale) and `area` was dropped entirely (no local equivalent) - added just after
  the rest of this rework, once it was clear `ApplicationUser` already carried that data. Only
  `stats`/`examSuggestions` and one `user` field with no local equivalent (`scoreCheckInfo`) still
  proxy gama-api, since no local content domain (past papers/multimedia/forum) exists yet;
  `legacyDataAvailable`/`legacyAuthRejected` now govern only that remainder. Two bugs found via live
  local testing during this same work and fixed before
  merge: (1) a native/local-token account's own token was being forwarded to gama-api as garbage,
  misread as a revoked session; (2) teacher/student endpoint selection trusted a possibly-stale local
  `Group` column instead of the live `group_id` claim already inside the forwarded JWT, causing the
  same false-401 for a real, valid session whose local `Group` had never synced.
- **Found while testing the above locally: `wwwroot/sitemap/` had the same tracked-runtime-output
  bug as `wwwroot/Files/`** (2026-09-02). `GlobalService.GenerateSiteMapAsync` (a daily Hangfire
  `RecurringJob`) deletes and regenerates every file under `wwwroot/sitemap/` at runtime - 16 files
  were committed there, so every run/deploy produced a spurious diff reflecting whichever
  local/dev database happened to be attached, not real content. Untracked and added to
  `.gitignore`, same fix as the `wwwroot/Files/` issue from 2026-08-22.
- **New proactive nudge system, first use case profile-completion prompts** (2026-09-02 — see
  [`docs/business/notifications.md`](docs/business/notifications.md), "Nudge system"): a daily
  Hangfire `RecurringJob` (`EvaluateAndSendNudges`) emails users who registered ≥7 days ago and are
  still missing a profile field (role, avatar, name, bio, skills, or experience), up to 3 times, 2
  weeks apart, always re-checking the condition before resending. New `NudgeTemplate` table
  (admin-editable via `api/v1/admin/nudges`, seeded with defaults by the migration) and
  `UserNudgeLog` (cooldown/cap tracking) - deliberately a separate system from the existing 19
  reactive/transactional email templates on `ApplicationSettingsDto` (ticket confirmations,
  subscription lifecycle, etc.), not a merge of the two; see the doc for why. School-photo nudging
  was considered and deliberately left out of this first batch - it doesn't fit the same
  "one field, set or not" shape as the others.
- **Nudge system: fixed a real spam complaint from sandbox, one day after the feature merged**
  (2026-09-02 - see `docs/business/notifications.md`, "Eligibility, cooldown, and send cap"): a
  long-registered user who'd never completed *any* profile field was getting one nudge email per
  missing field, all in the same nightly run - exactly the oldest sandbox accounts, since they'd
  had the most time to accumulate gaps without ever filling one in. Fixed with a new **global**
  cooldown (`MinDaysBetweenAnyNudge = 7`, `NudgeService`) checked across all `NudgeType`s together,
  not just the existing per-type one (14 days, same type only) - any two nudges to the same user,
  whatever their type, are now at least a week apart, so a user missing everything gets nudged
  about one field at a time instead of all of them at once.
- **Nudge system: candidate-pool caching, unsubscribe, and a per-run send cap** (2026-09-02, same
  day - see `docs/business/notifications.md`): three more additions before/just after this feature's
  first real production exposure. (1) `ApplicationUser.AllNudgesCompletedAt` - a one-way latch set
  the first time a user has zero remaining gaps, so the nightly job's candidate-pool query excludes
  already-complete users via one indexed null-check instead of re-scanning all six per-field text
  columns against them forever; added ahead of this app's ~30k production users making that scan
  cost real. (2) Unsubscribe: `ApplicationUser.NudgesOptedOutAt` (reversible, unlike the flag above),
  a one-click anonymous link (`GET nudges/unsubscribe`, token via `IDataProtectionProvider`,
  deliberately not Identity's own 10-day-default token provider - this link must keep working
  whenever an unread email finally gets opened) appended to every nudge email regardless of template
  content, plus an authenticated toggle (`PUT nudges/subscription`) so a user can opt back in. (3)
  `MaxSendsPerRun = 100` - a hard cap on total emails sent in one run, across every `NudgeType`
  combined, added before the very first production run against the existing ~30k users to avoid
  trying to send thousands of emails (and likely hitting the Resend email provider's own rate
  limits) in a single job execution; reuses the same 100 `ResendEmailProvider` already chunks
  recipient lists at, for consistency. The completeness check (1) deliberately still runs for every
  `NudgeType` even once the send cap is hit, so it stays accurate regardless of how much of the
  backlog a given run actually got to.
- **Nudge system: sent from the support inbox by mistake, fixed** (2026-09-03 - see
  `docs/business/notifications.md`, "Eligibility, cooldown, and send cap" point 5): `NudgeService`
  never set `From` on its `SendEmailRequestDto`, so `EmailService.SendEmailAsync` fell back to
  `GetSupportEmail()` - an automated, cyclical, up-to-3-times-per-type email arriving from Support
  reads as a person emailing you, and any reply would land in the support queue unhandled. Now
  explicitly `From = EmailService.GetNoReplyEmail()`, matching `SubscriptionService`/
  `IdentityService`'s existing convention for other automated/system emails.
- **`identities/profiles/list`'s `onlineStatus` was stale off login recency, and never set at all
  for legacy-bridge users - fixed** (2026-09-03 - see `docs/business/support-and-social.md`,
  `OnlineStatus`): `ApplicationUser.LastLoginDate` had exactly one write site in the whole codebase
  (`AddLoginHistoryAsync`, only called from the native login/token-issuing actions), so (1) a
  native-auth user's status decayed purely off time-since-login regardless of ongoing activity
  (10-day token lifespan), and (2) a legacy-auth-bridge user - still the frontend's primary auth
  path during the gama-api migration - never got `LastLoginDate` set at all and showed `NewUser`
  forever. Fixed with `IdentityService.TouchLastSeenAsync`, called from both `VerifyTokenAsync` and
  `VerifyLegacyTokenAsync` (the one chokepoint, `TokenAuthenticationHandler`, every authenticated
  request of either auth shape passes through), throttled via the Redis-backed `ICacheProvider` to
  roughly one SQL write per active user per 4-minute window rather than one per request.
- **Removed `POST identities/tokens/old`** (2026-09-03 - see `docs/api/authentication.md`): a
  temporary token-exchange endpoint explicitly commented `// this is temporary, must delete`,
  superseded by the legacy-auth-bridge and with no remaining callers. Removed along with its
  now-dead supporting code (`GenerateTokenByCoreTokenAsync`, `ICoreProvider.GetUserInformationAsync`,
  four DTOs/one ViewModel, the unused `Core:UserInfo` config key).
- **Removed `POST admin/identities/backfill-teacher-student-roles`** (2026-09-03 - see
  `docs/business/identity-and-access.md`, "One-time backfill for existing users"): a one-time-style
  Hangfire-backed backfill (added 2026-08-22) for the ~28,900 users who predated the automatic
  Role/ProfileVisibility sync now applied on every legacy login - already run to completion in
  production, following the same remove-after-completion pattern as the earlier avatar-conversion
  backfill.

## Documentation completeness

All six `docs/` subfolders (`architecture`, `business`, `database`, `api`, `development`,
`deployment`) are populated as of 2026-07-10. Treat this documentation as source of truth over
memory or assumption — but if you find it wrong, fix it in the same change, per `CLAUDE.md`.
