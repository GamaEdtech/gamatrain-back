# Project Snapshot

> High-level, point-in-time view of the system's current state. Update this file whenever
> architecture, database structure, APIs, business rules, infrastructure, or major workflows
> change significantly — see the "Living documentation" section of [`CLAUDE.md`](CLAUDE.md).
>
> Last updated: 2026-07-10, branch `feature/subscription-quotas`.

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
  bearer-auth), relaying the caller's raw legacy JWT straight from the `Authorization` header. This
  is the one legacy-bridge operation that *does* end a session early, closing the gap called out in
  the entry above.
- **Legacy-auth bridge logout blocklist added** (2026-07-14 — see
  [`docs/api/authentication.md`](docs/api/authentication.md)): the 2026-07-13 logout endpoint above
  only ended the session on gama-api's side — `ValidateLegacyJwtAsync` validates signature/issuer/
  audience/expiry entirely offline, with no way to know a token was just logged out, so the same
  JWT kept authenticating against *this* backend until its own `exp` naturally lapsed. Fixed by
  having `IdentityService.LegacyLogoutAsync` write the token (SHA-256-hashed, not raw) to
  `ICacheProvider`/Redis on a successful proxy logout, TTL'd to the token's own remaining lifetime;
  `VerifyLegacyTokenAsync` (per-request auth) and `GenerateTokenByCoreTokenAsync` (`tokens/old`)
  both check that blocklist right after signature validation. `SyncLegacyAuthAsync` (login/google)
  intentionally doesn't check it — a fresh login token can't already be blocklisted.
- **Quota-based subscription system built** (2026-07-10, phase 1 — see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)): `SubscriptionPlan` no
  longer carries a price — pricing moved to `SubscriptionPlanPrice` (regional-pricing-ready,
  gated dormant behind `Subscription:RegionalPricingEnabled`, default `false`) and quotas moved to
  a new `Feature`/`SubscriptionPlanFeature` catalog. Purchasing a plan reuses the existing
  Payment/gateway checkout flow (never the currency→points conversion); `games/spends` now tries
  subscription quota before falling back to wallet points, unchanged for non-subscribers.
  Deliberately deferred: PayPal, native recurring billing, a real FX source for base-currency
  reporting, and in-house pastpaper file serving.

## Documentation completeness

All six `docs/` subfolders (`architecture`, `business`, `database`, `api`, `development`,
`deployment`) are populated as of 2026-07-10. Treat this documentation as source of truth over
memory or assumption — but if you find it wrong, fix it in the same change, per `CLAUDE.md`.
