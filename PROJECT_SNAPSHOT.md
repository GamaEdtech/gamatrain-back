# Project Snapshot

> High-level, point-in-time view of the system's current state. Update this file whenever
> architecture, database structure, APIs, business rules, infrastructure, or major workflows
> change significantly — see the "Living documentation" section of [`CLAUDE.md`](CLAUDE.md).
>
> Last updated: 2026-08-13, branch `feat/subscription-usage-reporting`.

## What this system is

GamaEdtech Backend is a layered ASP.NET Core (.NET 10) REST API for the Gamatrain ed-tech
platform. It serves: a crowdsourced school directory with multi-dimension parent reviews, a blog,
a curriculum/exam content model, a gamified points ledger, crypto (Solana) + Stripe payments, a
quota-based subscription system (separate from the points ledger — see
[`docs/business/subscriptions.md`](docs/business/subscriptions.md)), and a support-ticket system.
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
  section): previously `SubscriptionPlanFeature.Limit` was plan-wide only — buying the Yearly
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
  show "you're on Monthly: 50, this plan's Yearly: 600" directly on the subscription screen.
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
  Active. Root cause: the quota-exhausted/insufficient-balance response
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

## Documentation completeness

All six `docs/` subfolders (`architecture`, `business`, `database`, `api`, `development`,
`deployment`) are populated as of 2026-07-10. Treat this documentation as source of truth over
memory or assumption — but if you find it wrong, fix it in the same change, per `CLAUDE.md`.
